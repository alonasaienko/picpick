# API зони B: завантаження й обробка

Ендпоінти відповідають FR-201–FR-206 з [вимог зони B](../03-srs/zone-b-processing.md). Формат помилок, обов'язкові поля й правила HTTP-кодів визначено в [конвенції помилок](conventions.md).

## Обсяг заглушок

Файли перевіряються лише за сигнатурою вмісту й розміром (порожній, формат, ліміт МБ), але нікуди не зберігаються, а обробки немає. Можливість декодування не перевіряється: пошкоджений файл із правильною сигнатурою буде прийнятий, а помилку декодування в цільовій системі виявить Worker і поставить фото статус `Failed` (FR-205). Галереї — тестові дані зони A ([zone-a.md](zone-a.md)), фото — тестові дані в пам'яті до перезапуску. Авторизацію й обмеження частоти не реалізовано: 401 і 429 описано в документації та Swagger.

## Ендпоінти

| ID | Метод і шлях | Вхід | Успішна відповідь | Помилки |
|---|---|---|---|---|
| B-201 | `POST /api/galleries/{galleryId}/photos` | `multipart/form-data`, поле `files`: до 50 файлів і до 1 ГБ | **200**, `accepted` (фото зі статусом `Uploaded`) і `rejected` (індекс, ім'я, `errorCode`, причина) | Спільні; **409** `GALLERY_NOT_EDITABLE`; **422** `PHOTO_ALL_FILES_REJECTED`, `PHOTO_BATCH_LIMIT_EXCEEDED`, `GALLERY_PHOTO_LIMIT_EXCEEDED` |
| B-202 | `GET /api/galleries/{galleryId}/progress` | UUID у шляху | **200**, `galleryStatus`, `total`, `ready`, `failed`, `inProgress` | Спільні; **409** `GALLERY_EXPIRED` |
| B-203 | `GET /api/galleries/{galleryId}/photos` | UUID у шляху | **200**, масив фото: `photoId`, `fileName`, `status`, `failureReason` (лише для `Failed`) | Спільні; **409** `GALLERY_EXPIRED` |
| B-204 | `POST /api/galleries/{galleryId}/photos/{photoId}/retry` | UUID галереї й фото | **200**, фото зі статусом `Uploaded` | Спільні; **404** `PHOTO_NOT_FOUND`; **409** `PHOTO_NOT_FAILED`, `GALLERY_NOT_EDITABLE` |

### Спільні помилки

| HTTP | errorCode | Коли виникає |
|---|---|---|
| 400 | `VALIDATION_FAILED` | Неправильний UUID; у запиті завантаження немає файлів |
| 401 | `UNAUTHENTICATED` | Фотограф не ввійшов |
| 404 | `GALLERY_NOT_FOUND` | Галерея відсутня або чужа — однакова відповідь (NFR-102) |
| 429 | `RATE_LIMIT_EXCEEDED` | Перевищено ліміт запитів; `Retry-After` — секунди |
| 500 | `INTERNAL_ERROR` | Помилка сервера; загальний текст і `traceId` |

### Коди відхилення окремих файлів (у списку `rejected` або в `errors`)

| errorCode | Коли |
|---|---|
| `PHOTO_FILE_EMPTY` | Файл порожній |
| `PHOTO_FORMAT_NOT_SUPPORTED` | Вміст не є JPEG, PNG, HEIC чи підтримуваним RAW, навіть якщо розширення `.jpg` |
| `PHOTO_FILE_TOO_LARGE` | JPEG, PNG чи HEIC понад 30 МБ, RAW понад 150 МБ |

## Ключові рішення

- **Неправильний, завеликий чи порожній файл.** Окремий файл — не помилка всього запиту: він потрапляє в `rejected`, а решта приймається (200). Якщо відхилено всі файли — **422** `PHOTO_ALL_FILES_REJECTED`, причини за індексом у `errors["files[i]"]`. Так визначено в конвенції (виняток для пакетного завантаження).
- **Фіналізована галерея.** Завантаження в `Shared`, `Finalized` чи `Expired` — **409** `GALLERY_NOT_EDITABLE`: дані коректні, але стан галереї не дозволяє дію.
- **Повтор для вже обробленого фото.** **409** `PHOTO_NOT_FAILED`: повторити можна лише `Failed` (FR-206).
- **Забагато файлів занадто швидко.** Понад 50 файлів або 1 ГБ в одному запиті — **422** `PHOTO_BATCH_LIMIT_EXCEEDED`. Забагато запитів за короткий час — **429** `RATE_LIMIT_EXCEEDED` (задача rate limiting). Запит понад ~1,01 ГБ сервер обриває ще до контролера.
- **Прострочена галерея.** Прогрес і список фото — **409** `GALLERY_EXPIRED`, без даних фото (FR-204). Метадані галереї доступні через API зони A.

## Перевірка через Swagger UI

Запустити `dotnet run --project src/webapp/WebApp.csproj --launch-profile http` і відкрити `http://localhost:5056/swagger`.

Тестові UUID мають вигляд `00000000-0000-0000-0000-000000000NNN`. Галереї: **101** Draft, **102** Processing, **103** Ready, **105** Finalized, **106** Expired, **999** відсутня. Фото в галереї 103: **201** Ready, **202** Failed.

| Перевірка | Дія | Очікування |
|---|---|---|
| Завантаження | POST photos для 101: один JPEG і один текстовий файл з розширенням `.jpg` | 200, JPEG у `accepted`, другий у `rejected` з `PHOTO_FORMAT_NOT_SUPPORTED` |
| 422 | POST photos для 103 лише з невалідними файлами | `PHOTO_ALL_FILES_REJECTED`, `errors["files[0]"]` |
| 409 | POST photos для 105 | `GALLERY_NOT_EDITABLE` |
| 404 | POST photos для 999 | `GALLERY_NOT_FOUND` |
| 400 | GET photos з `galleryId = abc` | `VALIDATION_FAILED` |
| Прогрес | GET progress для 103; для 106 | 200 з лічильниками; 409 `GALLERY_EXPIRED` |
| Повтор | POST retry для 103 / 201; потім для 103 / 202 | 409 `PHOTO_NOT_FAILED`; 200, фото `Uploaded` |
