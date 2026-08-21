mod shared_strings;
mod styles;
mod workbook;
mod worksheet;
mod xml;

use std::error::Error;
use std::fs::File;
use std::io::{self, Read, Seek, Write};
use std::path::Path;

use tempfile::NamedTempFile;
use zip::ZipArchive;

use shared_strings::SharedStringStore;
use styles::CellStyleStore;
use workbook::read_workbook;
use worksheet::{scan_worksheet, write_worksheet};

pub type ConversionResult<T> = Result<T, Box<dyn Error + Send + Sync>>;

#[derive(Clone, Debug)]
pub struct ConversionOptions {
    pub maximum_columns: usize,
    pub maximum_rows: usize,
    pub maximum_uncompressed_bytes: u64,
    pub maximum_package_bytes: u64,
    pub maximum_zip_entries: usize,
    pub maximum_compression_ratio: f64,
}

impl Default for ConversionOptions {
    fn default() -> Self {
        Self {
            maximum_columns: 16_384,
            maximum_rows: 1_048_576,
            maximum_uncompressed_bytes: 512 * 1024 * 1024,
            maximum_package_bytes: 256 * 1024 * 1024,
            maximum_zip_entries: 10_000,
            maximum_compression_ratio: 1_000.0,
        }
    }
}

pub struct XlsxConverter;

impl XlsxConverter {
    pub fn convert<R: Read, W: Write>(
        input: &mut R,
        output: &mut W,
        options: &ConversionOptions,
    ) -> ConversionResult<()> {
        validate_options(options)?;
        let mut package = NamedTempFile::new()?;
        copy_with_limit(input, &mut package, options.maximum_package_bytes)?;
        Self::convert_seekable(package.as_file_mut(), output, options)
    }

    pub fn convert_seekable<R: Read + Seek, W: Write>(
        input: &mut R,
        output: &mut W,
        options: &ConversionOptions,
    ) -> ConversionResult<()> {
        validate_options(options)?;
        let package_size = input.seek(io::SeekFrom::End(0))?;
        input.seek(io::SeekFrom::Start(0))?;
        if package_size > options.maximum_package_bytes {
            return Err(invalid_data(
                "The XLSX package exceeds the compressed size limit.",
            ));
        }

        let mut archive = ZipArchive::new(input)?;
        validate_archive(&mut archive, options)?;
        let workbook = read_workbook(&mut archive)?;
        let styles = CellStyleStore::load(&mut archive, workbook.uses_1904_date_system)?;
        let mut strings = SharedStringStore::load(&mut archive)?;
        let mut wrote_sheet = false;

        for sheet in workbook.sheets {
            let bounds = {
                let entry = archive.by_name(&sheet.path).map_err(|_| {
                    invalid_data(format!("Worksheet part was not found: {}", sheet.path))
                })?;
                scan_worksheet(entry, options)?
            };
            if bounds.is_empty() {
                continue;
            }

            if wrote_sheet {
                output.write_all(b"\r\n")?;
            }
            write!(output, "## {}\r\n\r\n", escape_heading(&sheet.name))?;

            let entry = archive.by_name(&sheet.path).map_err(|_| {
                invalid_data(format!("Worksheet part was not found: {}", sheet.path))
            })?;
            write_worksheet(entry, &bounds, &mut strings, &styles, output, options)?;
            wrote_sheet = true;
        }

        Ok(())
    }

    pub fn convert_file(
        input_path: impl AsRef<Path>,
        output_path: impl AsRef<Path>,
        options: &ConversionOptions,
    ) -> ConversionResult<()> {
        let mut input = File::open(input_path)?;
        let mut output = io::BufWriter::new(File::create(output_path)?);
        Self::convert_seekable(&mut input, &mut output, options)
    }
}

fn validate_archive<R: Read + Seek>(
    archive: &mut ZipArchive<R>,
    options: &ConversionOptions,
) -> ConversionResult<()> {
    if archive.len() > options.maximum_zip_entries {
        return Err(invalid_data(
            "The XLSX package exceeds the ZIP entry limit.",
        ));
    }

    let mut total = 0_u64;
    for index in 0..archive.len() {
        let entry = archive.by_index(index)?;
        total = total
            .checked_add(entry.size())
            .ok_or_else(|| invalid_data("The XLSX package exceeds the uncompressed size limit."))?;
        if total > options.maximum_uncompressed_bytes {
            return Err(invalid_data(
                "The XLSX package exceeds the uncompressed size limit.",
            ));
        }
        if entry.size() > 0
            && (entry.compressed_size() == 0
                || entry.size() as f64 / entry.compressed_size() as f64
                    > options.maximum_compression_ratio)
        {
            return Err(invalid_data(
                "An XLSX entry exceeds the compression ratio limit.",
            ));
        }
    }
    Ok(())
}

fn validate_options(options: &ConversionOptions) -> ConversionResult<()> {
    if options.maximum_columns == 0
        || options.maximum_rows == 0
        || options.maximum_uncompressed_bytes == 0
        || options.maximum_package_bytes == 0
        || options.maximum_zip_entries == 0
        || options.maximum_compression_ratio < 1.0
    {
        return Err(invalid_input("All conversion limits must be positive."));
    }
    Ok(())
}

fn copy_with_limit<R: Read, W: Write>(
    input: &mut R,
    output: &mut W,
    limit: u64,
) -> ConversionResult<()> {
    let mut buffer = [0_u8; 81_920];
    let mut total = 0_u64;
    loop {
        let read = input.read(&mut buffer)?;
        if read == 0 {
            return Ok(());
        }
        total += read as u64;
        if total > limit {
            return Err(invalid_data(
                "The XLSX package exceeds the compressed size limit.",
            ));
        }
        output.write_all(&buffer[..read])?;
    }
}

fn escape_heading(value: &str) -> String {
    value.replace('\\', "\\\\").replace('#', "\\#")
}

pub(crate) fn invalid_data(message: impl Into<String>) -> Box<dyn Error + Send + Sync> {
    Box::new(io::Error::new(io::ErrorKind::InvalidData, message.into()))
}

fn invalid_input(message: impl Into<String>) -> Box<dyn Error + Send + Sync> {
    Box::new(io::Error::new(io::ErrorKind::InvalidInput, message.into()))
}
