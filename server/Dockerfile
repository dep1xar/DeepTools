FROM python:3.12-slim

WORKDIR /app
COPY deeptools_telemetry.py /app/deeptools_telemetry.py

ENV DT_DB=/data/telemetry.db \
    DT_PORT=8787 \
    PYTHONUNBUFFERED=1

VOLUME ["/data"]
EXPOSE 8787

CMD ["python", "/app/deeptools_telemetry.py"]
