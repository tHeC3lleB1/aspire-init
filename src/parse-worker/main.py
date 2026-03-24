import base64
import io
import logging

import pdfplumber
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(title="ParseWorker", version="1.0.0")


class ParseRequest(BaseModel):
    file_bytes_base64: str
    content_type: str


class ParseResponse(BaseModel):
    text: str


@app.post("/parse", response_model=ParseResponse)
async def parse_document(request: ParseRequest) -> ParseResponse:
    """
    Accepts a base64-encoded file and returns its plain-text content.
    Supports PDF and plain-text formats.
    """
    try:
        file_bytes = base64.b64decode(request.file_bytes_base64)
    except Exception:
        raise HTTPException(status_code=400, detail="Invalid base64 encoding.")

    content_type = request.content_type.lower()

    if "pdf" in content_type:
        text = _extract_pdf_text(file_bytes)
    elif content_type.startswith("text/"):
        text = file_bytes.decode("utf-8", errors="replace")
    else:
        raise HTTPException(
            status_code=422,
            detail=f"Unsupported content type: {request.content_type}. "
                   "Supported: application/pdf, text/*",
        )

    logger.info("Parsed %d bytes (%s) → %d chars", len(file_bytes), content_type, len(text))
    return ParseResponse(text=text)


@app.get("/health")
async def health() -> dict:
    return {"status": "healthy"}


def _extract_pdf_text(file_bytes: bytes) -> str:
    pages: list[str] = []
    with pdfplumber.open(io.BytesIO(file_bytes)) as pdf:
        for page in pdf.pages:
            page_text = page.extract_text()
            if page_text:
                pages.append(page_text)
    return "\n\n".join(pages)
