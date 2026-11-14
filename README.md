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
| Local run | Podman Compose; the same Compose file works with Docker *(coming soon)* |

## Repository structure

```
docs/          project documentation, one folder per course task
src/webapp/    ASP.NET Core app: UI, REST API
src/worker/    Python worker: watermarks and previews
tests/         Web App error-handling checks and Worker tests
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

Create a watermarked preview from any photo (FR-203):

```bash
python src/worker/preview.py photo.jpg preview.jpg
```

Run the Worker tests:

```bash
pip install -r src/worker/requirements-dev.txt
pytest tests/worker
```

## Design

The UI has two colour themes. Visitors switch between them with the button in the header; the choice is remembered in the browser.

| Sunset (default) | Barbie |
|---|---|
| ![Sunset theme](docs/img/design/design-sunset.jpg) | ![Barbie theme](docs/img/design/design-barbie.jpg) |
| ![Sunset theme on a phone](docs/img/design/design-sunset-mobile.jpg) | ![Barbie theme on a phone](docs/img/design/design-barbie-mobile.jpg) |

| Theme | Palette | Feel |
|---|---|---|
| **Sunset** | `#1B1931` `#44174E` `#662249` `#A34054` `#ED9E59` `#E9BCB9` | Night sky from navy to plum with a soft apricot glow, blush text, apricot call-to-action |
| **Barbie** | `#E0218A` `#E94196` `#ED5C9B` `#F768A0` `#F18DBC` `#F7B9D7` `#FACDE5` `#FFECF6` | Hot-pink header, pearl gradient, script logo in Pacifico |

**How it works.**
- All colours are CSS variables in [`site.css`](src/webapp/wwwroot/css/site.css): the default theme lives in `:root`, the Barbie theme overrides the same variables under `[data-theme="pink"]`. Components only use variables, so a new theme is one more block of values.
- A couple of derived shades (for example `#C2187A` for Barbie buttons) are added where a palette colour does not reach WCAG AA contrast for text.
- Fonts: **Unbounded** for headings, **Manrope** for text, **Pacifico** for the Barbie logo — all from Google Fonts with Cyrillic support.
- Animated sparkles twinkle in the background of key sections; they are decorative, ignore clicks and stay still when the system asks to reduce motion.
- The layout is responsive: the menu collapses below 768 px, and every page fits a 375 px phone screen without horizontal scrolling.
- Pages that are not built yet (for example `/Galleries/Create` or `/Account/Login`) show a friendly 404 page; API routes keep their JSON errors.

![404 page for a page that is not built yet](docs/img/design/design-404.jpg)

## Documentation

All design documents live in [`docs/`](docs/). Start with the [glossary](docs/glossary.md).

## Team

| Member | Area |
|---|---|
| Yuliia | Zone A: accounts and galleries |
| Alona | Zone B: upload and photo processing |
| Uliana | Zone C: client access and selection |