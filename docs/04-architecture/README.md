# П.4 Архітектура

PicPick побудовано з двох сервісів: **Web App** (ASP.NET Core) відповідає за сторінки, API й доступ, а **Worker** (Python) у фоні створює прев'ю з вотермаркою. Сервіси спілкуються асинхронно через **RabbitMQ**, дані зберігаються в **PostgreSQL**, файли — у **MinIO**. Інтерфейс — серверні сторінки Razor Pages. Прототип запускається в Docker Compose, цільове розгортання описано окремо.

Усі діаграми написано як код у Mermaid і рендеряться прямо на GitHub.

## Компоненти

- [Компоненти системи](components.md) — короткий перелік; повна компонентна діаграма — у [High-level Architecture](../03-srs/high-level-architecture.md) (SRS, п.3).

## Sequence-діаграми головних потоків

| Зона | Потік | Файл |
|---|---|---|
| A | Вхід фотографа і створення галереї | [flow-zone-a.md](flow-zone-a.md) |
| B | Завантаження й фонова обробка фото, прогрес | [flow-zone-b.md](flow-zone-b.md) |
| C | Вибір клієнта за посиланням | [flow-zone-c.md](flow-zone-c.md) |

## Архітектурні рішення (ADR)

| ADR | Рішення | Файл |
|---|---|---|
| ADR-001 | UI на Razor Pages замість SPA | [001-ui-razor-pages.md](adr/001-ui-razor-pages.md) |
| ADR-002 | Окремий Worker і черга замість моноліту | [002-microservices.md](adr/002-microservices.md) |
| ADR-003 | Хмарне розгортання і serverless | [003-cloud-serverless.md](adr/003-cloud-serverless.md) |

Кожен ADR має чотири розділи: контекст, розглянуті варіанти, рішення й наслідки.
