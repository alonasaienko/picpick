# API зони C: доступ клієнта і вибір

Ендпоінти відповідають FR-301–FR-307 з [вимог зони C](../03-srs/zone-c-client.md). Формат помилок, обов'язкові поля й правила HTTP-кодів визначено в [конвенції помилок](conventions.md). Назви сутностей, статусів і шляхів — за [глосарієм](../glossary.md).

## Обсяг заглушок

Цей файл фіксує контракт заглушки `ShareController` для перевірки через Swagger UI. Заглушка повертає фіксовані тестові дані й не зберігає зміни між запитами. Ситуацію визначає значення токена або `id` у запиті (див. «Перевірка через Swagger UI»). Автентифікація фотографа в заглушці не реалізована: 401 описано в контракті та Swagger. Ліміт частоти використовує спільну політику `default`.

## Ендпоінти

Ендпоінти фотографа вимагають входу (cookie), ендпоінти клієнта — лише `Token` у шляху. Усі успішні JSON-поля мають camelCase.

| ID | Актор | Метод і шлях | Вхід | Успішна відповідь | Помилки |
|---|---|---|---|---|---|
| C-301 | Фотограф | `POST /api/galleries/{galleryId}/share-links` | UUID галереї в шляху; опційне JSON `{ "expiresAt": "<UTC datetime>" }` | **201**, `shareLinkId`, `galleryId`, `token`, `url`, `state` (`Active`), `expiresAt`; галерея `Ready` переходить у `Shared` | Спільні; **409** `GALLERY_NOT_SHAREABLE`; **422** `SHARE_LINK_EXPIRES_AT_INVALID` |
| C-302 | Фотограф | `POST /api/galleries/{galleryId}/share-links/{shareLinkId}/revoke` | UUID галереї й посилання в шляху; тіла немає | **200**, `shareLinkId`, `galleryId`, `state` (`Revoked`); повторне відкликання `Revoked` також успішне | Спільні; **404** `SHARE_LINK_NOT_FOUND`; **409** `SHARE_LINK_NOT_ACTIVE` для `Expired` |
| C-303 | Клієнт | `GET /api/share-links/{token}/previews` | `Token` у шляху | **200**, `galleryName`, `nickname`, `galleryStatus`, `selectionStatus`, `expiresAt`, `likedCount`, `previews[]` (`photoId`, `previewUrl`, `liked`) | Спільні; **404** `SHARE_LINK_INVALID` для будь-якого недійсного токена |
| C-304a | Клієнт | `PUT /api/share-links/{token}/likes/{photoId}` | `Token` і UUID фото в шляху; тіла немає | **200**, `photoId`, `liked` (`true`), `likedCount` | Спільні; **404** `SHARE_LINK_INVALID`, `PHOTO_NOT_FOUND`; **409** `SELECTION_ALREADY_SUBMITTED`, `GALLERY_FINALIZED` |
| C-304b | Клієнт | `DELETE /api/share-links/{token}/likes/{photoId}` | `Token` і UUID фото в шляху; тіла немає | **200**, `photoId`, `liked` (`false`), `likedCount`; зняття відсутнього лайка також успішне | Спільні; **404** `SHARE_LINK_INVALID`, `PHOTO_NOT_FOUND`; **409** `SELECTION_ALREADY_SUBMITTED`, `GALLERY_FINALIZED` |
| C-304c | Клієнт | `GET /api/share-links/{token}/likes?since={opSeq}` | `Token` у шляху; опційний курсор `since` | **200**, зміни лайків після `opSeq`, поточний `opSeq` | Спільні; **404** `SHARE_LINK_INVALID` |
| C-305 | Клієнт | `POST /api/share-links/{token}/selection/submit` | `Token` у шляху; тіла немає | **200**, `status` (`Submitted`), початковий `submittedAt`, `likedCount`, `likedPhotoIds[]`; повторна відправка повертає той самий результат | Спільні; **404** `SHARE_LINK_INVALID`; **409** `SELECTION_EMPTY`, `GALLERY_FINALIZED` |
| C-306 | Фотограф | `GET /api/galleries/{galleryId}/selection` | UUID галереї в шляху | **200**, `galleryId`, `status` (`Open` чи `Submitted`), `submittedAt`, `count`, `items[]` (`photoId`, `fileName`, `previewUrl`) | Спільні; **404** `SELECTION_NOT_FOUND`, `GALLERY_NOT_FOUND`; **409** `GALLERY_EXPIRED` |

Повторна відправка C-305 є ідемпотентною успішною операцією, а не помилкою. Операції C-304 є ідемпотентними: `PUT` встановлює стан лайка, `DELETE` знімає його. Такий контракт відповідає [CS-302 LWW element set](../06-concurrency/cs-302-lww-element-set.md).

### Спільні помилки

| HTTP | `errorCode` | Коли виникає | Де |
|---|---|---|---|
| 400 | `VALIDATION_FAILED` | Неправильний UUID (`galleryId`, `shareLinkId`, `photoId`), некоректний `since`, зламаний JSON або відсутнє тіло, якщо воно передбачене контрактом | C-301, C-302, C-304, C-306 |
| 401 | `UNAUTHENTICATED` | Фотограф не ввійшов | C-301, C-302, C-306 |
| 404 | `GALLERY_NOT_FOUND` | Галерея відсутня або чужа — однакова відповідь (NFR-102) | C-301, C-302, C-306 |
| 429 | `RATE_LIMIT_EXCEEDED` | Перевищено ліміт спільної політики `default` (120 запитів за хвилину); `Retry-After` — секунди | усі |
| 500 | `INTERNAL_ERROR` | Непередбачена помилка сервера; загальний текст і `traceId` | усі |

### Власні помилки зони C

| HTTP | `errorCode` | Коли виникає |
|---|---|---|
| 404 | `SHARE_LINK_INVALID` | Невідомий, пошкоджений, прострочений або відкликаний токен, а також токен галереї зі станом `Expired`. Для всіх причин відповідь однакова, без даних галереї |
| 404 | `SELECTION_NOT_FOUND` | Для галереї ще немає посилання, отже клієнт не міг створити вибір |
| 404 | `PHOTO_NOT_FOUND` | Фото не належить галереї за посиланням або не має статусу `Ready` |
| 409 | `GALLERY_NOT_SHAREABLE` | Посилання створюють для галереї `Draft`, `Processing`, `Finalized` або `Expired` (FR-301 п.4) |
| 409 | `GALLERY_FINALIZED` | Галерею закрито фотографом (FR-106): лайки й відправка вибору заборонені |
| 409 | `GALLERY_EXPIRED` | Термін дії галереї минув; список вибору недоступний (C-306) |
| 409 | `SHARE_LINK_NOT_ACTIVE` | Відкликають посилання у стані `Expired`; `Revoked` відкликається ідемпотентно успішно |
| 409 | `SELECTION_ALREADY_SUBMITTED` | Вибір уже відправлено; застосовується до зміни лайків, але не до повторної відправки C-305 |
| 409 | `SELECTION_EMPTY` | Відправка вибору без жодного лайка |
| 422 | `SHARE_LINK_EXPIRES_AT_INVALID` | `expiresAt` не пізніше поточного UTC або пізніше за `ExpiresAt` галереї |

## Ключові рішення

- **Недійсне посилання.** Невідомий, пошкоджений, прострочений або відкликаний токен завжди дає **404 `SHARE_LINK_INVALID`**. Відповідь не розкриває, чи існувало посилання, і не містить назви галереї, нікнейма, кількості фото, прев'ю, `Selection` чи причини недійсності. Це відповідає FR-307 і NFR-301.
- **Створення нового посилання.** Для галереї одночасно діє не більше одного `Active`-посилання. Створення нового посилання переводить попереднє `Active` у `Revoked`, старий токен одразу перестає діяти, а `Selection` зберігається. Другий активний ShareLink не повертає `SHARE_LINK_ALREADY_EXISTS`.
- **Термін дії.** Якщо `expiresAt` не задано, використовується менше з «+14 діб від створення» і `ExpiresAt` галереї. Значення зберігається в UTC, має бути пізніше поточного часу й не пізніше терміну галереї.
- **Відкликання.** Повторне відкликання `Revoked` посилання повертає 200 без змін. `Expired` посилання не можна відкликати повторно й воно дає `SHARE_LINK_NOT_ACTIVE`.
- **Лайки та синхронізація.** Кожен запит змінює один елемент. `PUT` і `DELETE` не передають список лайків, тому паралельні зміни різних фото не перезаписують одна одну. `GET .../likes?since=` повертає журнал змін після курсора.
- **Спільні стани.** Лайк після відправки вибору та лайк у фіналізованій галереї — 409. Повторна відправка вже `Submitted` — успіх із початковим `submittedAt`, без повторної зміни стану.
- **Фото, яких клієнт не бачить.** Фото не в `Ready` не потрапляє до прев'ю, а операція над ним повертає `PHOTO_NOT_FOUND`. Оригінали й прямі адреси MinIO клієнту не повертаються.
- **Ліміти.** Для запитів зони C діє політика `default` — 120 запитів за хвилину з однієї IP-адреси. Після перевищення повертається 429 і `Retry-After`. Окремий ліміт NFR-301 для недійсних токенів ще не реалізований у заглушці.
- **Безпека і приватність.** Токен не потрапляє в `instance` або `detail`; заглушка використовує безпечний `urn:request:{traceId}` в `instance`. Нікнейм повертається як текст без виконання HTML, email фотографа не повертається ніколи.

## Що бачить клієнт без акаунта

Клієнт не входить у систему, тому 401 і 403 для нього не застосовуються. Для будь-якого недійсного токена API повертає 404 `SHARE_LINK_INVALID`, а сторінка не містить жодних даних галереї.

| Ситуація | HTTP і `errorCode` | Що показує сторінка клієнта | Що клієнт може зробити |
|---|---|---|---|
| Токен невідомий або пошкоджений | 404 `SHARE_LINK_INVALID` | «Це посилання більше не діє. Зверніться до вашого фотографа по нове» | Перевірити повне посилання або звернутися до фотографа |
| Термін посилання або галереї минув | 404 `SHARE_LINK_INVALID` | Те саме повідомлення, без пояснення причини | Попросити фотографа надіслати нове посилання |
| Посилання відкликано | 404 `SHARE_LINK_INVALID` | Те саме повідомлення, без пояснення причини | Попросити фотографа надіслати нове посилання |
| Галерея фіналізована | 409 `GALLERY_FINALIZED` на лайк чи відправку | Прев'ю лишаються видимими, кнопки лайка вимкнено | Лише переглядати |
| Вибір уже відправлено | 409 для зміни лайка; повторна відправка — 200 | Кнопки лайка вимкнено, повідомлення «Вибір відправлено» | Лише переглядати |

## Перевірка через Swagger UI

Цей розділ є сценарієм для заглушки `ShareController`. Запустити `dotnet run --project src/webapp/WebApp.csproj --launch-profile http` і відкрити `http://localhost:5056/swagger`. Розділ — `Share`.

**Ендпоінти фотографа.** UUID мають вигляд `00000000-0000-0000-0000-000000000NNN`. Галереї: **101** `Draft`, **102** `Processing`, **103** `Ready`, **104** `Shared`, **105** `Finalized`, **106** `Expired`, **999** відсутня. Посилання: **401** `Active`, **402** `Revoked`, **403** `Expired`.

**Ендпоінти клієнта.** Токени: `demo-open` (чинне, 2 лайки), `demo-empty` (чинне, 0 лайків), `demo-submitted` (вибір відправлено), `demo-finalized` (галерея фіналізована), `demo-expired`, `demo-revoked`. Будь-який інший токен — `SHARE_LINK_INVALID`. Фото: **301**, **302**, **303** (`Ready`); будь-який інший UUID — `PHOTO_NOT_FOUND`.

| Перевірка | Дія | Очікування |
|---|---|---|
| Створення | C-301 для `103` без тіла | 201, галерея переходить у `Shared` |
| Створення з датою | C-301 з `expiresAt` у минулому або після терміну галереї | 422 `SHARE_LINK_EXPIRES_AT_INVALID` |
| Створення нового | C-301 для `104` з активним `401` | 201, `401` стає `Revoked`, `Selection` зберігається |
| Неприпустимий стан | C-301 для `101`, `102`, `105`, `106` | 409 `GALLERY_NOT_SHAREABLE` |
| 404 галерея | C-301 для `999` | 404 `GALLERY_NOT_FOUND` |
| 400 | C-301 з `galleryId = abc` | 400 `VALIDATION_FAILED` |
| Відкликання | C-302 для активного `401`, потім для `402` | 200 `Revoked` в обох випадках |
| Відкликання простроченого | C-302 для `403` | 409 `SHARE_LINK_NOT_ACTIVE` |
| Недійсний токен | C-303 з `demo-expired`, `demo-revoked`, `abc` | 404 `SHARE_LINK_INVALID` для всіх |
| Встановлення лайка | C-304a з `demo-open` і фото `301` | 200, `liked: true` |
| Зняття лайка | C-304b з `demo-open` і фото `301` | 200, `liked: false` |
| Лайк, помилки | `demo-submitted`; `demo-finalized`; невідоме фото | 409 `SELECTION_ALREADY_SUBMITTED`; 409 `GALLERY_FINALIZED`; 404 `PHOTO_NOT_FOUND` |
| Відправка | C-305 з `demo-open` і `demo-empty` | 200 для непорожнього; 409 `SELECTION_EMPTY` для порожнього |
| Повторна відправка | C-305 з `demo-submitted` | 200 з початковим `submittedAt` |
| Вибір фотографа | C-306 для `104`, `103`, `106`, `999` | 200; 404 `SELECTION_NOT_FOUND`; 409 `GALLERY_EXPIRED`; 404 `GALLERY_NOT_FOUND` |
| 429 | 121 запит за хвилину з однієї IP-адреси до будь-якого endpoint-а зони C | 429 `RATE_LIMIT_EXCEEDED` з `Retry-After` |

## Приклади Problem Details

У всіх прикладах `type` — зафіксоване посилання на відповідний розділ RFC 9110. Значення `<traceId>` — плейсхолдер: у реальній відповіді Swagger його замінює ідентифікатор конкретного запиту. Реакція клієнта не повинна показувати стек викликів, внутрішні шляхи або секретний токен.

### C-301. Невалідний `expiresAt`

```http
POST /api/galleries/00000000-0000-0000-0000-000000000103/share-links
Content-Type: application/json

{ "expiresAt": "2026-01-01T00:00:00Z" }
```

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.21",
  "title": "Дані не відповідають правилам",
  "status": 422,
  "detail": "Термін дії посилання має бути пізніше поточного часу й не пізніше терміну дії галереї.",
  "instance": "urn:request:<traceId>",
  "errorCode": "SHARE_LINK_EXPIRES_AT_INVALID",
  "traceId": "<traceId>",
  "errors": {
    "expiresAt": [
      "Укажіть майбутню дату не пізніше терміну дії галереї."
    ]
  }
}
```

### C-302. Відкликати посилання

```http
POST /api/galleries/00000000-0000-0000-0000-000000000104/share-links/00000000-0000-0000-0000-000000000403/revoke
```

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Конфлікт стану ресурсу",
  "status": 409,
  "detail": "Прострочене посилання не можна відкликати.",
  "instance": "urn:request:<traceId>",
  "errorCode": "SHARE_LINK_NOT_ACTIVE",
  "traceId": "<traceId>"
}
```

### C-303. Недійсне посилання

```http
GET /api/share-links/abc/previews
```

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Ресурс не знайдено",
  "status": 404,
  "detail": "Це посилання більше не діє. Зверніться до вашого фотографа по нове.",
  "instance": "urn:request:<traceId>",
  "errorCode": "SHARE_LINK_INVALID",
  "traceId": "<traceId>"
}
```

Реакція клієнта: показати сторінку без назви галереї, нікнейма, кількості фото, прев'ю, `Selection` і причини недійсності.

### C-304a. Встановити лайк

```http
PUT /api/share-links/demo-open/likes/00000000-0000-0000-0000-000000000301
```

```json
{
  "photoId": "00000000-0000-0000-0000-000000000301",
  "liked": true,
  "likedCount": 2
}
```

### C-304b. Зняти лайк

```http
DELETE /api/share-links/demo-open/likes/00000000-0000-0000-0000-000000000301
```

```json
{
  "photoId": "00000000-0000-0000-0000-000000000301",
  "liked": false,
  "likedCount": 1
}
```

Обидві операції виконуються безпечним повторенням. Після помилки клієнт повертає кнопку в попередній стан і показує «Не вдалося зберегти, спробуйте ще раз».

### C-304c. Синхронізувати лайки

```http
GET /api/share-links/demo-open/likes?since=1
```

Успішна відповідь містить `opSeq` і масив змін:

```json
{
  "opSeq": 2,
  "changes": [
    {
      "photoId": "00000000-0000-0000-0000-000000000302",
      "liked": true,
      "opSeq": 2
    }
  ]
}
```

### C-305. Відправити вибір повторно

```http
POST /api/share-links/demo-submitted/selection/submit
```

Результат — **200**, початковий `submittedAt`, `status: "Submitted"` і той самий список `likedPhotoIds`. Повторне email-сповіщення не надсилається.

```json
{
  "status": "Submitted",
  "submittedAt": "2026-10-25T12:00:00+00:00",
  "likedCount": 2,
  "likedPhotoIds": [
    "00000000-0000-0000-0000-000000000301",
    "00000000-0000-0000-0000-000000000302"
  ]
}
```

### C-306. Вибір відсутній

```http
GET /api/galleries/00000000-0000-0000-0000-000000000103/selection
```

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Ресурс не знайдено",
  "status": 404,
  "detail": "Для цієї галереї ще немає вибору: посилання для клієнта не створено.",
  "instance": "urn:request:<traceId>",
  "errorCode": "SELECTION_NOT_FOUND",
  "traceId": "<traceId>"
}
```

Реакція в кабінеті фотографа: показати «Клієнт ще не отримав посилання» із кнопкою «Створити посилання».

### Інші помилки зони C

Кожен endpoint повертає один об'єкт `ApiProblemDetails`, а не масив. Нижче наведено повні приклади відповідей у форматі Swagger; `traceId` та `instance` показані як плейсхолдери, бо генеруються для кожного запиту.

#### `PHOTO_NOT_FOUND`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Ресурс не знайдено",
  "status": 404,
  "detail": "Фото не знайдено.",
  "instance": "urn:request:<traceId>",
  "errorCode": "PHOTO_NOT_FOUND",
  "traceId": "<traceId>"
}
```

#### `GALLERY_NOT_SHAREABLE`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Конфлікт стану ресурсу",
  "status": 409,
  "detail": "Посилання можна створити лише для галереї зі станом Ready або Shared, термін дії якої ще не минув.",
  "instance": "urn:request:<traceId>",
  "errorCode": "GALLERY_NOT_SHAREABLE",
  "traceId": "<traceId>"
}
```

#### `GALLERY_FINALIZED`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Конфлікт стану ресурсу",
  "status": 409,
  "detail": "Фотограф завершив відбір у цій галереї, лайки більше не приймаються.",
  "instance": "urn:request:<traceId>",
  "errorCode": "GALLERY_FINALIZED",
  "traceId": "<traceId>"
}
```

#### `GALLERY_EXPIRED`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Конфлікт стану ресурсу",
  "status": 409,
  "detail": "Термін дії галереї минув.",
  "instance": "urn:request:<traceId>",
  "errorCode": "GALLERY_EXPIRED",
  "traceId": "<traceId>"
}
```

#### `SHARE_LINK_NOT_ACTIVE`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Конфлікт стану ресурсу",
  "status": 409,
  "detail": "Прострочене посилання не можна відкликати.",
  "instance": "urn:request:<traceId>",
  "errorCode": "SHARE_LINK_NOT_ACTIVE",
  "traceId": "<traceId>"
}
```

#### `SHARE_LINK_NOT_FOUND`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Ресурс не знайдено",
  "status": 404,
  "detail": "Посилання не знайдено в цій галереї.",
  "instance": "urn:request:<traceId>",
  "errorCode": "SHARE_LINK_NOT_FOUND",
  "traceId": "<traceId>"
}
```

#### `SELECTION_ALREADY_SUBMITTED`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Конфлікт стану ресурсу",
  "status": 409,
  "detail": "Вибір уже відправлено, змінювати його не можна.",
  "instance": "urn:request:<traceId>",
  "errorCode": "SELECTION_ALREADY_SUBMITTED",
  "traceId": "<traceId>"
}
```

#### `SELECTION_EMPTY`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Конфлікт стану ресурсу",
  "status": 409,
  "detail": "Спочатку оберіть хоча б один кадр.",
  "instance": "urn:request:<traceId>",
  "errorCode": "SELECTION_EMPTY",
  "traceId": "<traceId>"
}
```
