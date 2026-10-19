# PicPick

A web service that helps photographers and their clients choose photos together.

The photographer uploads a folder of raw shots. The system generates watermarked previews in the background. The client opens a private link, browses the previews and likes the ones they want. The photographer then gets the list of selected shots for final editing.

Team project for the **Object-Oriented Analysis and Design** course.

## Tech stack

| Layer | Technology |
|---|---|
| UI + API | ASP.NET Core 8 (Razor Pages, Web API, Swagger) |
| Photo processing | Python 3.12 (Pillow, pika, boto3) |
| Message broker | RabbitMQ |
| Database | PostgreSQL |
| File storage | MinIO (S3-compatible) |
| Local run | Docker Compose *(coming soon)* |

## Repository structure

```
docs/          project documentation, one folder per course task
src/webapp/    ASP.NET Core app: UI, REST API
src/worker/    Python worker: watermarks and previews
```

## Getting started

### Web App

Requires the .NET 8 SDK.

```bash
dotnet run --project src/webapp
```

Then open the URL printed in the console:
- `/` shows the main page;
- `/swagger` shows the API documentation;
- `/health` shows the health check.

### Worker

Requires Python 3.10+.

```bash
python3 -m venv src/worker/.venv
source src/worker/.venv/bin/activate
pip install -r src/worker/requirements.txt
python src/worker/main.py
```

## Documentation

All design documents live in [`docs/`](docs/). Start with the [glossary](docs/glossary.md).

## Team

| Member | Area |
|---|---|
| Yuliia | Zone A: accounts and galleries |
| Alona | Zone B: upload and photo processing |
| Uliana | Zone C: client access and selection |