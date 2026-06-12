import argparse
import logging
import sys


def sanitize_error(error):
    name = error.__class__.__name__.lower()
    allowed = "abcdefghijklmnopqrstuvwxyz0123456789-"
    name = "".join(character if character in allowed else "-" for character in name)
    return f"faster-whisper-runtime-{name}"[:120]


def compute_attempts(compute_type):
    normalized = (compute_type or "auto").strip().lower()

    if normalized in ("float16", "int8_float16"):
        return [("cuda", normalized), ("cpu", "int8")]

    if normalized == "float32":
        return [("cpu", "float32")]

    if normalized == "int8":
        return [("cuda", "int8_float16"), ("cpu", "int8")]

    return [("cuda", "auto"), ("cpu", "int8")]


def transcribe(args):
    from faster_whisper import WhisperModel

    language = None if args.language.lower() == "auto" else args.language
    last_error = None

    for device, compute_type in compute_attempts(args.compute_type):
        try:
            model = WhisperModel(
                args.model_dir,
                device=device,
                compute_type=compute_type,
                local_files_only=True,
            )
            segments, _ = model.transcribe(
                args.audio_file,
                language=language,
                beam_size=5,
                vad_filter=False,
                without_timestamps=True,
            )
            return "".join(segment.text for segment in segments).strip()
        except Exception as error:
            last_error = error

    raise last_error if last_error is not None else RuntimeError("transcription failed")


def main():
    parser = argparse.ArgumentParser(description="Run faster-whisper transcription for Tts.App.")
    parser.add_argument("--model-dir", required=True)
    parser.add_argument("--audio-file", required=True)
    parser.add_argument("--language", required=True)
    parser.add_argument("--compute-type", required=True)
    args = parser.parse_args()

    logging.disable(logging.CRITICAL)

    try:
        transcript = transcribe(args)
    except KeyboardInterrupt:
        print("faster-whisper-runtime-canceled", file=sys.stderr)
        return 130
    except Exception as error:
        print(sanitize_error(error), file=sys.stderr)
        return 1

    sys.stdout.write(transcript)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())