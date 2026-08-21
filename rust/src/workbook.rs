use std::collections::HashMap;
use std::io::{BufReader, Read, Seek};

use quick_xml::Reader;
use quick_xml::events::Event;
use zip::ZipArchive;

use crate::xml::{attribute, local_name};
use crate::{ConversionResult, invalid_data};

pub(crate) struct SheetInfo {
    pub(crate) name: String,
    pub(crate) path: String,
}

pub(crate) struct WorkbookInfo {
    pub(crate) sheets: Vec<SheetInfo>,
    pub(crate) uses_1904_date_system: bool,
}

pub(crate) fn read_workbook<R: Read + Seek>(
    archive: &mut ZipArchive<R>,
) -> ConversionResult<WorkbookInfo> {
    let relationships = read_relationships(archive)?;
    let entry = archive
        .by_name("xl/workbook.xml")
        .map_err(|_| invalid_data("The file is not a valid XLSX workbook."))?;
    let mut reader = Reader::from_reader(BufReader::new(entry));
    let mut buffer = Vec::new();
    let mut result = WorkbookInfo {
        sheets: Vec::new(),
        uses_1904_date_system: false,
    };

    loop {
        match reader.read_event_into(&mut buffer)? {
            Event::Start(element) | Event::Empty(element) => {
                match local_name(element.name().as_ref()) {
                    b"workbookPr" => {
                        let value = attribute(&reader, &element, b"date1904")?.unwrap_or_default();
                        result.uses_1904_date_system =
                            value == "1" || value.eq_ignore_ascii_case("true");
                    }
                    b"sheet" => {
                        let relationship_id = attribute(&reader, &element, b"id")?
                            .ok_or_else(|| invalid_data("A worksheet relationship is missing."))?;
                        let path = relationships
                            .get(&relationship_id)
                            .ok_or_else(|| invalid_data("A worksheet relationship is missing."))?
                            .clone();
                        result.sheets.push(SheetInfo {
                            name: attribute(&reader, &element, b"name")?
                                .unwrap_or_else(|| "Sheet".to_owned()),
                            path,
                        });
                    }
                    _ => {}
                }
            }
            Event::Eof => break,
            _ => {}
        }
        buffer.clear();
    }
    Ok(result)
}

fn read_relationships<R: Read + Seek>(
    archive: &mut ZipArchive<R>,
) -> ConversionResult<HashMap<String, String>> {
    let entry = archive
        .by_name("xl/_rels/workbook.xml.rels")
        .map_err(|_| invalid_data("The file is not a valid XLSX workbook."))?;
    let mut reader = Reader::from_reader(BufReader::new(entry));
    let mut buffer = Vec::new();
    let mut result = HashMap::new();

    loop {
        match reader.read_event_into(&mut buffer)? {
            Event::Start(element) | Event::Empty(element)
                if local_name(element.name().as_ref()) == b"Relationship" =>
            {
                if attribute(&reader, &element, b"TargetMode")?
                    .is_some_and(|value| value.eq_ignore_ascii_case("external"))
                {
                    buffer.clear();
                    continue;
                }
                if let (Some(id), Some(target)) = (
                    attribute(&reader, &element, b"Id")?,
                    attribute(&reader, &element, b"Target")?,
                ) {
                    result.insert(id, resolve_part_path(&target)?);
                }
            }
            Event::Eof => break,
            _ => {}
        }
        buffer.clear();
    }
    Ok(result)
}

fn resolve_part_path(target: &str) -> ConversionResult<String> {
    let normalized = target.replace('\\', "/");
    let rooted = normalized.starts_with('/');
    let mut safe_parts: Vec<&str> = if rooted { Vec::new() } else { vec!["xl"] };
    for part in normalized.trim_start_matches('/').split('/') {
        match part {
            "" | "." => {}
            ".." => {
                if safe_parts.pop().is_none() {
                    return Err(invalid_data(
                        "A package relationship escapes the XLSX package.",
                    ));
                }
            }
            _ => safe_parts.push(part),
        }
    }
    Ok(safe_parts.join("/"))
}
