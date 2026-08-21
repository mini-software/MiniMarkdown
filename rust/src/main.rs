use std::env;
use std::fs::File;
use std::io::{self, BufWriter, Write};
use std::process::ExitCode;

use minimarkdown::{ConversionOptions, XlsxConverter};

fn main() -> ExitCode {
    let args: Vec<String> = env::args().skip(1).collect();
    if args.is_empty()
        || args
            .iter()
            .any(|argument| argument == "--help" || argument == "-h")
    {
        println!("Usage: minimarkdown <input.xlsx|-> [-o output.md]");
        return if args.is_empty() {
            ExitCode::from(2)
        } else {
            ExitCode::SUCCESS
        };
    }

    let output_path = args
        .iter()
        .skip(1)
        .position(|argument| argument == "-o")
        .map(|index| index + 2)
        .and_then(|index| args.get(index));
    if args.iter().skip(1).any(|argument| argument == "-o") && output_path.is_none() {
        eprintln!("Missing output path after -o.");
        return ExitCode::from(2);
    }

    match run(&args[0], output_path.map(String::as_str)) {
        Ok(()) => ExitCode::SUCCESS,
        Err(error) => {
            eprintln!("Conversion failed: {error}");
            ExitCode::FAILURE
        }
    }
}

fn run(input_path: &str, output_path: Option<&str>) -> minimarkdown::ConversionResult<()> {
    let mut output: Box<dyn Write> = match output_path {
        Some(path) => Box::new(BufWriter::new(File::create(path)?)),
        None => Box::new(BufWriter::new(io::stdout().lock())),
    };
    let options = ConversionOptions::default();
    if input_path == "-" {
        XlsxConverter::convert(&mut io::stdin().lock(), &mut output, &options)
    } else {
        XlsxConverter::convert_seekable(&mut File::open(input_path)?, &mut output, &options)
    }
}
