FROM python:3.12-alpine
WORKDIR /app
COPY pyproject.toml ./
COPY src/ src/
ENV PYTHONPATH=/app
CMD ["python", "-m", "src.main"]
