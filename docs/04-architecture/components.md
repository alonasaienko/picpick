# Компоненти системи

Компонентна діаграма, таблиця компонентів і ключові архітектурні принципи вже описані в розділі SRS **[2.7 High-level Architecture](../03-srs/high-level-architecture.md)**. Щоб не дублювати, тут лише короткий перелік.

| Компонент | Роль | Технологія |
|---|---|---|
| Браузер | Інтерфейс фотографа й клієнта | Razor Pages + vanilla JavaScript |
| Web App | Сторінки, REST API, вхід, перевірка доступу, фонові задачі | ASP.NET Core 8 |
| Worker | Створення прев'ю з вотермаркою | Python 3.12 |
| RabbitMQ | Черга задач (Web App → Worker) і Черга подій (Worker → Web App) | RabbitMQ 3, AMQP |
| PostgreSQL | Основна база даних, пише лише Web App | PostgreSQL 16 |
| MinIO | Оригінали й прев'ю | S3-сумісне сховище |

Чому система поділена саме так, пояснено в [ADR-002](adr/002-microservices.md).
