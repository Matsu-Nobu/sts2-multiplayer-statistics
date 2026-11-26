# 評価サーバー (docs/spec/card-evaluation.md)。models/ に ONNX のモデルを置いてからビルドする (README.md)
FROM python:3.12-slim-bookworm

WORKDIR /app
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY sts2env/ ./sts2env/
COPY sts2rl/ ./sts2rl/
COPY sts2eval/ ./sts2eval/
COPY data/ ./data/
COPY models/ ./models/

RUN useradd --system --no-create-home app
USER app
ENV PORT=8080 PYTHONUNBUFFERED=1
EXPOSE 8080
CMD ["python", "-m", "sts2eval.server"]
