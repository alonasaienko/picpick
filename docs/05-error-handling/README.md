# Пункт 5. Обробка помилок і OpenAPI

У цьому розділі описано єдиний контракт помилок REST API PicPick та
покриття HTTP-кодів у його OpenAPI-специфікації.

## Документи розділу

- [Конвенція формату помилок](conventions.md) — формат `Problem Details`, обов'язкові поля,
  правила вибору HTTP-коду та правила використання помилок у контролерах.
- [Зона A: галереї](zone-a.md) — створення, перегляд, фіналізація та видалення галерей.
- [Зона B: завантаження й обробка](zone-b.md) — завантаження фотографій, прогрес,
  список фотографій і повторна обробка.
- [Зона C: доступ клієнта і вибір](zone-c.md) — share links, прев'ю, лайки та вибір.
- [OpenAPI-специфікація](openapi.json) — знімок документа, згенерованого застосунком.

## Як переглянути API

### Swagger UI під час запуску застосунку

У корені репозиторію виконайте:

```powershell
dotnet run --project src\webapp\WebApp.csproj --launch-profile http
```

Після запуску відкрийте <http://localhost:5056/swagger>. Swagger UI показує всі
операції, параметри, моделі та оголошені відповіді з кодами помилок.

Сира специфікація доступна за адресою
<http://localhost:5056/swagger/v1/swagger.json>.

### Swagger Editor

Відкрийте <https://editor.swagger.io/>, виберіть `File` → `Import File` і
завантажте [`openapi.json`](openapi.json). У редакторі потрібно перевірити, що:

- немає повідомлень `Parser error`, `Structural error` або `Resolver error`;
- відображаються всі операції зон A, B і C та health-check;
- для операцій відображаються оголошені відповіді з кодами.

## Зведене покриття кодів

Позначка `✓` означає, що код присутній у секції `responses` відповідної
операції в [`openapi.json`](openapi.json). Порожня клітинка означає, що цей
код для операції не оголошений.

| Ендпоінт | 400 | 403 | 404 | 409 | 422 | 429 |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| `POST /api/galleries` | ✓ | ✓ |  |  | ✓ | ✓ |
| `GET /api/galleries` | ✓ | ✓ |  |  |  | ✓ |
| `GET /api/galleries/{id}` | ✓ | ✓ | ✓ |  |  | ✓ |
| `POST /api/galleries/{id}/finalize` | ✓ | ✓ | ✓ | ✓ |  | ✓ |
| `DELETE /api/galleries/{id}` | ✓ | ✓ | ✓ |  |  | ✓ |
| `POST /api/galleries/{galleryId}/photos` | ✓ |  | ✓ | ✓ | ✓ | ✓ |
| `GET /api/galleries/{galleryId}/progress` | ✓ |  | ✓ | ✓ |  | ✓ |
| `GET /api/galleries/{galleryId}/photos` | ✓ |  | ✓ | ✓ |  | ✓ |
| `POST /api/galleries/{galleryId}/photos/{photoId}/retry` | ✓ |  | ✓ | ✓ |  | ✓ |
| `POST /api/galleries/{galleryId}/share-links` | ✓ |  | ✓ | ✓ | ✓ | ✓ |
| `POST /api/galleries/{galleryId}/share-links/{shareLinkId}/revoke` | ✓ |  | ✓ | ✓ |  | ✓ |
| `GET /api/share-links/{token}/previews` |  |  | ✓ |  |  | ✓ |
| `PUT /api/share-links/{token}/likes/{photoId}` |  |  | ✓ | ✓ |  | ✓ |
| `DELETE /api/share-links/{token}/likes/{photoId}` |  |  | ✓ | ✓ |  | ✓ |
| `GET /api/share-links/{token}/likes` | ✓ |  | ✓ |  |  | ✓ |
| `POST /api/share-links/{token}/selection/submit` |  |  | ✓ | ✓ |  | ✓ |
| `GET /api/galleries/{galleryId}/selection` | ✓ |  | ✓ | ✓ |  | ✓ |
| `GET /health` |  |  |  |  |  |  |

Усього в OpenAPI описано **18 ендпоінтів**: 5 ендпоінтів зони A,
4 ендпоінти зони B, 8 ендпоінтів зони C і health-check.

## Перевірка обов'язкових кодів

У специфікації присутні всі шість обов'язкових кодів:

| Код | Приклади ендпоінтів |
|---:|---|
| `400` | `POST /api/galleries`, `POST /api/galleries/{galleryId}/photos` |
| `403` | `GET /api/galleries`, `GET /api/galleries/{id}` |
| `404` | `GET /api/galleries/{id}`, `GET /api/share-links/{token}/previews` |
| `409` | `POST /api/galleries/{id}/finalize`, операції з фотографіями та лайками |
| `422` | `POST /api/galleries`, завантаження фотографій, створення share link |
| `429` | Операції галерей, фотографій і share links |

Таким чином, вимога щодо покриття кодів `400`, `403`, `404`, `409`, `422` і
`429` виконана. Окремо в OpenAPI також описані успішні відповіді, `401`,
`415` і `500`, де вони передбачені контрактом.