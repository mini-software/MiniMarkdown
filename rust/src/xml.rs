use quick_xml::Reader;
use quick_xml::events::{BytesStart, BytesText};

use crate::ConversionResult;

pub(crate) fn local_name(name: &[u8]) -> &[u8] {
    name.rsplit(|byte| *byte == b':').next().unwrap_or(name)
}

pub(crate) fn attribute(
    reader: &Reader<impl std::io::BufRead>,
    element: &BytesStart<'_>,
    name: &[u8],
) -> ConversionResult<Option<String>> {
    for attribute in element.attributes().with_checks(false) {
        let attribute = attribute?;
        if local_name(attribute.key.as_ref()) == name {
            return Ok(Some(
                attribute
                    .decode_and_unescape_value(reader.decoder())?
                    .into_owned(),
            ));
        }
    }
    Ok(None)
}

pub(crate) fn text_value(text: &BytesText<'_>) -> ConversionResult<String> {
    Ok(text.xml_content()?.into_owned())
}
