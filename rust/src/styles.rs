use std::collections::HashMap;
use std::io::{BufReader, Read, Seek};

use quick_xml::Reader;
use quick_xml::events::Event;
use zip::ZipArchive;

use crate::ConversionResult;
use crate::xml::{attribute, local_name};

#[derive(Clone, Copy)]
enum CellNumberKind {
    Number,
    Date,
    DateTime,
    Time,
    Duration,
}

pub(crate) struct CellStyleStore {
    styles: Vec<CellNumberKind>,
    uses_1904_date_system: bool,
}

impl CellStyleStore {
    pub(crate) fn load<R: Read + Seek>(
        archive: &mut ZipArchive<R>,
        uses_1904_date_system: bool,
    ) -> ConversionResult<Self> {
        let mut store = Self {
            styles: vec![CellNumberKind::Number],
            uses_1904_date_system,
        };
        let Ok(entry) = archive.by_name("xl/styles.xml") else {
            return Ok(store);
        };
        let mut custom_formats = HashMap::new();
        let mut reader = Reader::from_reader(BufReader::new(entry));
        let mut buffer = Vec::new();
        let mut in_cell_formats = false;

        loop {
            match reader.read_event_into(&mut buffer)? {
                Event::Start(element) | Event::Empty(element) => {
                    match local_name(element.name().as_ref()) {
                        b"numFmt" => {
                            if let Some(id) = attribute(&reader, &element, b"numFmtId")?
                                .and_then(|value| value.parse::<u32>().ok())
                            {
                                custom_formats.insert(
                                    id,
                                    attribute(&reader, &element, b"formatCode")?
                                        .unwrap_or_default(),
                                );
                            }
                        }
                        b"cellXfs" => in_cell_formats = true,
                        b"xf" if in_cell_formats => {
                            let id = attribute(&reader, &element, b"numFmtId")?
                                .and_then(|value| value.parse::<u32>().ok())
                                .unwrap_or(0);
                            store.styles.push(classify(id, custom_formats.get(&id)));
                        }
                        _ => {}
                    }
                }
                Event::End(element) if local_name(element.name().as_ref()) == b"cellXfs" => {
                    in_cell_formats = false;
                }
                Event::Eof => break,
                _ => {}
            }
            buffer.clear();
        }
        if store.styles.len() > 1 {
            store.styles.remove(0);
        }
        Ok(store)
    }

    pub(crate) fn format(&self, style_index: usize, value: f64, original: &str) -> String {
        let Some(kind) = self.styles.get(style_index).copied() else {
            return original.to_owned();
        };
        match kind {
            CellNumberKind::Number => original.to_owned(),
            CellNumberKind::Duration => format_duration(value),
            kind => format_date_time(value, self.uses_1904_date_system, kind),
        }
    }
}

fn classify(id: u32, format: Option<&String>) -> CellNumberKind {
    match id {
        14..=17 => return CellNumberKind::Date,
        18..=21 | 45 | 47 => return CellNumberKind::Time,
        22 => return CellNumberKind::DateTime,
        46 => return CellNumberKind::Duration,
        _ => {}
    }

    let mut code = String::new();
    let mut quoted = false;
    for character in format.map(String::as_str).unwrap_or_default().chars() {
        if character == '"' {
            quoted = !quoted;
        } else if !quoted {
            code.extend(character.to_lowercase());
        }
    }
    if code.contains("[h]") || code.contains("[m]") || code.contains("[s]") {
        return CellNumberKind::Duration;
    }
    let has_date = code.contains('y') || code.contains('d');
    let has_time = code.contains('h') || code.contains('s');
    match (has_date, has_time) {
        (true, true) => CellNumberKind::DateTime,
        (true, false) => CellNumberKind::Date,
        (false, true) => CellNumberKind::Time,
        (false, false) => CellNumberKind::Number,
    }
}

fn format_duration(value: f64) -> String {
    let total_seconds = (value * 86_400.0).round() as i64;
    let sign = if total_seconds < 0 { "-" } else { "" };
    let absolute = total_seconds.unsigned_abs();
    format!(
        "{}{hours}:{minutes:02}:{seconds:02}",
        sign,
        hours = absolute / 3_600,
        minutes = absolute / 60 % 60,
        seconds = absolute % 60
    )
}

fn format_date_time(value: f64, uses_1904: bool, kind: CellNumberKind) -> String {
    let epoch_days = if uses_1904 { -24_107 } else { -25_569 };
    let whole_days = value.floor() as i64;
    let mut seconds = ((value - value.floor()) * 86_400.0).round() as i64;
    let mut days = epoch_days + whole_days;
    if seconds >= 86_400 {
        days += 1;
        seconds -= 86_400;
    }
    let (year, month, day) = civil_from_days(days);
    let hour = seconds / 3_600;
    let minute = seconds / 60 % 60;
    let second = seconds % 60;
    match kind {
        CellNumberKind::Date => format!("{year:04}-{month:02}-{day:02}"),
        CellNumberKind::Time => format!("{hour:02}:{minute:02}:{second:02}"),
        _ => format!("{year:04}-{month:02}-{day:02} {hour:02}:{minute:02}:{second:02}"),
    }
}

fn civil_from_days(days_since_1970: i64) -> (i64, i64, i64) {
    let days = days_since_1970 + 719_468;
    let era = if days >= 0 { days } else { days - 146_096 } / 146_097;
    let day_of_era = days - era * 146_097;
    let year_of_era =
        (day_of_era - day_of_era / 1_460 + day_of_era / 36_524 - day_of_era / 146_096) / 365;
    let mut year = year_of_era + era * 400;
    let day_of_year = day_of_era - (365 * year_of_era + year_of_era / 4 - year_of_era / 100);
    let month_prime = (5 * day_of_year + 2) / 153;
    let day = day_of_year - (153 * month_prime + 2) / 5 + 1;
    let month = month_prime + if month_prime < 10 { 3 } else { -9 };
    year += i64::from(month <= 2);
    (year, month, day)
}

#[cfg(test)]
mod tests {
    use super::{CellNumberKind, format_date_time};

    #[test]
    fn formats_the_1904_date_system_epoch() {
        assert_eq!(
            format_date_time(0.0, true, CellNumberKind::Date),
            "1904-01-01"
        );
    }
}
