use std::io::{self, Cursor, Read, Write};

use minimarkdown::{ConversionOptions, XlsxConverter};
use zip::write::SimpleFileOptions;
use zip::{CompressionMethod, ZipWriter};

#[test]
fn converts_common_cell_types_and_sparse_rows() {
    let mut package = Cursor::new(create_workbook());
    let mut output = Vec::new();
    XlsxConverter::convert_seekable(&mut package, &mut output, &ConversionOptions::default())
        .unwrap();

    assert_eq!(
        String::from_utf8(output).unwrap(),
        "## Data\r\n\r\n\
         | Name | Note |  | Active | Date |\r\n\
         | --- | --- | --- | --- | --- |\r\n\
         | Alice | A\\|B<br>C |  | TRUE | 2023-03-15 |\r\n\
         |  |  | 42.5 |  |  |\r\n"
    );
}

#[test]
fn converts_non_seekable_input_and_leaves_it_usable() {
    let mut input = ReadOnlyCursor(Cursor::new(create_workbook()));
    let mut output = Vec::new();
    XlsxConverter::convert(&mut input, &mut output, &ConversionOptions::default()).unwrap();
    assert!(String::from_utf8(output).unwrap().contains("| Alice |"));
    assert_eq!(input.read(&mut [0_u8; 1]).unwrap(), 0);
}

#[test]
fn rejects_packages_over_resource_limits() {
    let bytes = create_workbook();
    let mut package = Cursor::new(bytes);
    let mut output = Vec::new();
    let options = ConversionOptions {
        maximum_package_bytes: 1,
        ..ConversionOptions::default()
    };
    let error = XlsxConverter::convert_seekable(&mut package, &mut output, &options).unwrap_err();
    assert!(error.to_string().contains("compressed size limit"));
}

#[test]
fn rejects_malformed_packages() {
    let mut package = Cursor::new(b"not an xlsx package".to_vec());
    let mut output = Vec::new();
    assert!(
        XlsxConverter::convert_seekable(&mut package, &mut output, &ConversionOptions::default(),)
            .is_err()
    );
}

#[test]
fn writes_a_large_worksheet_incrementally() {
    const ROWS: usize = 20_000;
    let mut package = Cursor::new(create_large_workbook(ROWS));
    let mut output = CountingWriter::default();
    XlsxConverter::convert_seekable(&mut package, &mut output, &ConversionOptions::default())
        .unwrap();
    assert_eq!(output.line_count, ROWS + 3);
    assert!(output.maximum_write_length < 64);
}

fn create_workbook() -> Vec<u8> {
    let mut archive = ZipWriter::new(Cursor::new(Vec::new()));
    add(
        &mut archive,
        "[Content_Types].xml",
        r#"<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/></Types>"#,
    );
    add(
        &mut archive,
        "xl/workbook.xml",
        r#"<?xml version="1.0"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><workbookPr date1904="0"/><sheets><sheet name="Data" sheetId="1" r:id="rId1"/></sheets></workbook>"#,
    );
    add(
        &mut archive,
        "xl/_rels/workbook.xml.rels",
        r#"<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>"#,
    );
    add(
        &mut archive,
        "xl/sharedStrings.xml",
        "<?xml version=\"1.0\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>Name</t></si><si><t>Alice</t></si><si><r><t>A|B</t></r><r><t>\nC</t></r></si></sst>",
    );
    add(
        &mut archive,
        "xl/styles.xml",
        r#"<?xml version="1.0"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><cellXfs count="2"><xf numFmtId="0"/><xf numFmtId="14"/></cellXfs></styleSheet>"#,
    );
    add(
        &mut archive,
        "xl/worksheets/sheet1.xml",
        r#"<?xml version="1.0"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="inlineStr"><is><t>Note</t></is></c><c r="D1" t="inlineStr"><is><t>Active</t></is></c><c r="E1" t="inlineStr"><is><t>Date</t></is></c></row><row r="2"><c r="A2" t="s"><v>1</v></c><c r="B2" t="s"><v>2</v></c><c r="D2" t="b"><v>1</v></c><c r="E2" s="1"><v>45000</v></c></row><row r="3"><c r="C3"><v>42.5</v></c></row></sheetData></worksheet>"#,
    );
    archive.finish().unwrap().into_inner()
}

fn create_large_workbook(rows: usize) -> Vec<u8> {
    let mut archive = ZipWriter::new(Cursor::new(Vec::new()));
    add(
        &mut archive,
        "xl/workbook.xml",
        r#"<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Large" sheetId="1" r:id="rId1"/></sheets></workbook>"#,
    );
    add(
        &mut archive,
        "xl/_rels/workbook.xml.rels",
        r#"<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>"#,
    );
    let options = SimpleFileOptions::default().compression_method(CompressionMethod::Stored);
    archive
        .start_file("xl/worksheets/sheet1.xml", options)
        .unwrap();
    write!(
        archive,
        "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
    )
    .unwrap();
    for row in 1..=rows {
        write!(
            archive,
            "<row r=\"{row}\"><c r=\"A{row}\"><v>{row}</v></c></row>"
        )
        .unwrap();
    }
    write!(archive, "</sheetData></worksheet>").unwrap();
    archive.finish().unwrap().into_inner()
}

fn add(archive: &mut ZipWriter<Cursor<Vec<u8>>>, path: &str, content: &str) {
    let options = SimpleFileOptions::default().compression_method(CompressionMethod::Stored);
    archive.start_file(path, options).unwrap();
    archive.write_all(content.as_bytes()).unwrap();
}

struct ReadOnlyCursor(Cursor<Vec<u8>>);

impl Read for ReadOnlyCursor {
    fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
        self.0.read(buffer)
    }
}

#[derive(Default)]
struct CountingWriter {
    line_count: usize,
    maximum_write_length: usize,
}

impl Write for CountingWriter {
    fn write(&mut self, buffer: &[u8]) -> io::Result<usize> {
        self.maximum_write_length = self.maximum_write_length.max(buffer.len());
        self.line_count += buffer.iter().filter(|byte| **byte == b'\n').count();
        Ok(buffer.len())
    }

    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}
