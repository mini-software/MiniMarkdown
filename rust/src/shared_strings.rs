use std::io::{BufReader, Read, Seek};

#[cfg(not(target_arch = "wasm32"))]
use std::io::{self, SeekFrom, Write};

use quick_xml::Reader;
use quick_xml::events::Event;
#[cfg(not(target_arch = "wasm32"))]
use tempfile::NamedTempFile;
use zip::ZipArchive;

use crate::xml::{local_name, text_value};
use crate::{ConversionResult, invalid_data};

pub(crate) struct SharedStringStore {
    #[cfg(not(target_arch = "wasm32"))]
    file: NamedTempFile,
    #[cfg(not(target_arch = "wasm32"))]
    offsets: Vec<u64>,
    #[cfg(target_arch = "wasm32")]
    values: Vec<String>,
}

impl SharedStringStore {
    pub(crate) fn load<R: Read + Seek>(archive: &mut ZipArchive<R>) -> ConversionResult<Self> {
        let mut store = Self {
            #[cfg(not(target_arch = "wasm32"))]
            file: NamedTempFile::new()?,
            #[cfg(not(target_arch = "wasm32"))]
            offsets: Vec::new(),
            #[cfg(target_arch = "wasm32")]
            values: Vec::new(),
        };
        let Ok(entry) = archive.by_name("xl/sharedStrings.xml") else {
            return Ok(store);
        };
        let mut reader = Reader::from_reader(BufReader::new(entry));
        let mut buffer = Vec::new();
        let mut item: Option<String> = None;
        let mut in_text = false;

        loop {
            match reader.read_event_into(&mut buffer)? {
                Event::Start(element) => match local_name(element.name().as_ref()) {
                    b"si" => item = Some(String::new()),
                    b"t" if item.is_some() => in_text = true,
                    _ => {}
                },
                Event::Text(text) if in_text => {
                    item.as_mut().unwrap().push_str(&text_value(&text)?);
                }
                Event::CData(text) if in_text => {
                    item.as_mut().unwrap().push_str(&text.decode()?);
                }
                Event::End(element) => match local_name(element.name().as_ref()) {
                    b"t" => in_text = false,
                    b"si" => store.write(item.take().unwrap_or_default())?,
                    _ => {}
                },
                Event::Eof => break,
                _ => {}
            }
            buffer.clear();
        }
        Ok(store)
    }

    pub(crate) fn get(&mut self, index_text: &str) -> ConversionResult<String> {
        let index: usize = index_text
            .parse()
            .map_err(|_| invalid_data("A shared string index is invalid."))?;
        #[cfg(target_arch = "wasm32")]
        return self
            .values
            .get(index)
            .cloned()
            .ok_or_else(|| invalid_data("A shared string index is invalid."));

        #[cfg(not(target_arch = "wasm32"))]
        {
            let offset = *self
                .offsets
                .get(index)
                .ok_or_else(|| invalid_data("A shared string index is invalid."))?;
            let file = self.file.as_file_mut();
            file.seek(SeekFrom::Start(offset))?;
            let mut length = [0_u8; 8];
            file.read_exact(&mut length)?;
            let length = u64::from_le_bytes(length);
            let length = usize::try_from(length)
                .map_err(|_| invalid_data("A shared string is too large."))?;
            let mut bytes = vec![0_u8; length];
            file.read_exact(&mut bytes)?;
            String::from_utf8(bytes).map_err(|error| Box::new(error) as _)
        }
    }

    #[cfg(not(target_arch = "wasm32"))]
    fn write(&mut self, value: String) -> io::Result<()> {
        let file = self.file.as_file_mut();
        self.offsets.push(file.stream_position()?);
        file.write_all(&(value.len() as u64).to_le_bytes())?;
        file.write_all(value.as_bytes())
    }

    #[cfg(target_arch = "wasm32")]
    fn write(&mut self, value: String) -> ConversionResult<()> {
        self.values.push(value);
        Ok(())
    }
}
