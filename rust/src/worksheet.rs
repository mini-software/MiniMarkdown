use std::collections::BTreeMap;
use std::io::{BufReader, Read, Write};

use quick_xml::Reader;
use quick_xml::events::Event;
use zip::read::ZipFile;

use crate::shared_strings::SharedStringStore;
use crate::styles::CellStyleStore;
use crate::xml::{attribute, local_name, text_value};
use crate::{ConversionOptions, ConversionResult, invalid_data};

pub(crate) struct WorksheetBounds {
    first_row: usize,
    last_row: usize,
    first_column: usize,
    last_column: usize,
}

impl WorksheetBounds {
    pub(crate) fn is_empty(&self) -> bool {
        self.last_row == 0
    }
}

pub(crate) fn scan_worksheet<R: Read>(
    entry: ZipFile<'_, R>,
    options: &ConversionOptions,
) -> ConversionResult<WorksheetBounds> {
    let mut bounds = WorksheetBounds {
        first_row: usize::MAX,
        last_row: 0,
        first_column: usize::MAX,
        last_column: 0,
    };
    visit_cells(entry, options, |row, column, _, _, value| {
        if !value.is_empty() {
            bounds.first_row = bounds.first_row.min(row);
            bounds.last_row = bounds.last_row.max(row);
            bounds.first_column = bounds.first_column.min(column);
            bounds.last_column = bounds.last_column.max(column);
        }
        Ok(())
    })?;
    Ok(bounds)
}

pub(crate) fn write_worksheet<R: Read, W: Write>(
    entry: ZipFile<'_, R>,
    bounds: &WorksheetBounds,
    strings: &mut SharedStringStore,
    styles: &CellStyleStore,
    output: &mut W,
    options: &ConversionOptions,
) -> ConversionResult<()> {
    let mut row_values = BTreeMap::new();
    let mut current_row = bounds.first_row;
    let mut header_written = false;
    visit_cells(entry, options, |row, column, cell_type, style, value| {
        if row < bounds.first_row || row > bounds.last_row {
            return Ok(());
        }
        while row > current_row {
            write_row(output, &row_values, bounds.first_column, bounds.last_column)?;
            if !header_written {
                write_separator(output, bounds.last_column - bounds.first_column + 1)?;
                header_written = true;
            }
            row_values.clear();
            current_row += 1;
        }
        row_values.insert(
            column,
            format_value(cell_type, style, value, strings, styles)?,
        );
        Ok(())
    })?;

    while current_row <= bounds.last_row {
        write_row(output, &row_values, bounds.first_column, bounds.last_column)?;
        if !header_written {
            write_separator(output, bounds.last_column - bounds.first_column + 1)?;
            header_written = true;
        }
        row_values.clear();
        current_row += 1;
    }
    Ok(())
}

fn visit_cells<R: Read, F>(
    entry: ZipFile<'_, R>,
    options: &ConversionOptions,
    mut visitor: F,
) -> ConversionResult<()>
where
    F: FnMut(usize, usize, &str, usize, &str) -> ConversionResult<()>,
{
    let mut reader = Reader::from_reader(BufReader::new(entry));
    let mut buffer = Vec::new();
    let mut inferred_row = 0;
    let mut current_row = 0;
    let mut inferred_column = 0;
    let mut current_cell: Option<(usize, String, usize, String)> = None;
    let mut capture_value = false;
    let mut capture_inline_text = false;

    loop {
        match reader.read_event_into(&mut buffer)? {
            Event::Start(element) => match local_name(element.name().as_ref()) {
                b"row" => {
                    inferred_row += 1;
                    current_row = positive_usize(attribute(&reader, &element, b"r")?, inferred_row);
                    inferred_row = current_row;
                    inferred_column = 0;
                    if current_row > options.maximum_rows {
                        return Err(invalid_data("The worksheet exceeds the row limit."));
                    }
                }
                b"c" => {
                    let column = match attribute(&reader, &element, b"r")? {
                        Some(reference) => parse_column(&reference)?,
                        None => inferred_column + 1,
                    };
                    inferred_column = column;
                    if column > options.maximum_columns {
                        return Err(invalid_data("The worksheet exceeds the column limit."));
                    }
                    let cell_type = attribute(&reader, &element, b"t")?.unwrap_or_default();
                    let style = positive_usize(attribute(&reader, &element, b"s")?, 0);
                    current_cell = Some((column, cell_type, style, String::new()));
                }
                b"v" if current_cell.is_some() => capture_value = true,
                b"t" if current_cell
                    .as_ref()
                    .is_some_and(|cell| cell.1 == "inlineStr") =>
                {
                    capture_inline_text = true;
                }
                _ => {}
            },
            Event::Text(text) if capture_value || capture_inline_text => {
                current_cell
                    .as_mut()
                    .unwrap()
                    .3
                    .push_str(&text_value(&text)?);
            }
            Event::CData(text) if capture_value || capture_inline_text => {
                current_cell.as_mut().unwrap().3.push_str(&text.decode()?);
            }
            Event::End(element) => match local_name(element.name().as_ref()) {
                b"v" => capture_value = false,
                b"t" => capture_inline_text = false,
                b"c" => {
                    if let Some((column, cell_type, style, value)) = current_cell.take() {
                        visitor(current_row, column, &cell_type, style, &value)?;
                    }
                }
                _ => {}
            },
            Event::Eof => break,
            _ => {}
        }
        buffer.clear();
    }
    Ok(())
}

fn format_value(
    cell_type: &str,
    style: usize,
    value: &str,
    strings: &mut SharedStringStore,
    styles: &CellStyleStore,
) -> ConversionResult<String> {
    match cell_type {
        "s" => strings.get(value),
        "b" => Ok(if value == "1" { "TRUE" } else { "FALSE" }.to_owned()),
        "" => match value.parse::<f64>() {
            Ok(number) => Ok(styles.format(style, number, value)),
            Err(_) => Ok(value.to_owned()),
        },
        _ => Ok(value.to_owned()),
    }
}

fn write_row<W: Write>(
    output: &mut W,
    values: &BTreeMap<usize, String>,
    first_column: usize,
    last_column: usize,
) -> ConversionResult<()> {
    write!(output, "|")?;
    for column in first_column..=last_column {
        write!(
            output,
            " {} |",
            escape_cell(values.get(&column).map(String::as_str).unwrap_or_default())
        )?;
    }
    output.write_all(b"\r\n")?;
    Ok(())
}

fn write_separator<W: Write>(output: &mut W, columns: usize) -> ConversionResult<()> {
    write!(output, "|")?;
    for _ in 0..columns {
        write!(output, " --- |")?;
    }
    output.write_all(b"\r\n")?;
    Ok(())
}

fn escape_cell(value: &str) -> String {
    value
        .replace('\\', "\\\\")
        .replace('|', "\\|")
        .replace("\r\n", "<br>")
        .replace(['\r', '\n'], "<br>")
}

fn positive_usize(text: Option<String>, fallback: usize) -> usize {
    text.and_then(|value| value.parse().ok())
        .filter(|value| *value > 0)
        .unwrap_or(fallback)
}

fn parse_column(reference: &str) -> ConversionResult<usize> {
    let mut column = 0_usize;
    for byte in reference
        .bytes()
        .take_while(|byte| byte.is_ascii_uppercase())
    {
        column = column
            .checked_mul(26)
            .and_then(|value| value.checked_add((byte - b'A' + 1) as usize))
            .ok_or_else(|| invalid_data("A cell reference is invalid."))?;
    }
    if column == 0 {
        return Err(invalid_data("A cell reference is invalid."));
    }
    Ok(column)
}
