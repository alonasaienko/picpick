param([string]$BaseUrl = 'http://localhost:5056')
$ErrorActionPreference = 'Stop'
$checks = 0

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

function Send-Api([string]$Method, [string]$Path, $Body = $null, [string]$ContentType = 'application/json') {
    $arguments = @{ Uri = $BaseUrl + $Path; Method = $Method; SkipHttpErrorCheck = $true }
    if ($null -ne $Body) {
        $arguments.Body = [Text.Encoding]::UTF8.GetBytes([string]$Body)
        $arguments.ContentType = $ContentType
    }
    $response = Invoke-WebRequest @arguments
    $text = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { $response.Content }
    $data = if ($text) { $text | ConvertFrom-Json } else { $null }
    return @{ Status = [int]$response.StatusCode; Data = $data; Headers = $response.Headers; Text = $text }
}

function Assert-Problem($Response, [int]$Status, [string]$Code) {
    Assert-True ($Response.Status -eq $Status) "Expected HTTP $Status, got $($Response.Status): $($Response.Text)"
    Assert-True ($Response.Data.status -eq $Status) 'Problem status differs from HTTP status'
    Assert-True ($Response.Data.errorCode -eq $Code) "Expected errorCode $Code"
    Assert-True ([string]$Response.Headers['Content-Type'] -like 'application/problem+json*') 'Incorrect error media type'
    foreach ($field in @('type', 'title', 'detail', 'instance', 'traceId')) {
        Assert-True (-not [string]::IsNullOrEmpty($Response.Data.$field)) "Missing Problem Details field: $field"
    }
    Assert-True (-not $Response.Data.instance.Contains('?')) 'instance leaks query string'
}

$future = [DateTimeOffset]::UtcNow.AddDays(1).ToString('O')
$payload = @{ name = '  Smoke зйомка  '; expiresAt = $future } | ConvertTo-Json
$created = Send-Api POST '/api/galleries' $payload
Assert-True ($created.Status -eq 201) 'Create must return 201'
Assert-True ($created.Data.name -eq 'Smoke зйомка') 'Name must be trimmed'
Assert-True ($created.Data.status -eq 'Draft' -and $created.Data.photoCount -eq 0) 'New Gallery must be empty Draft'
$id = $created.Data.galleryId
try {
    Assert-True ([string]$created.Headers.Location -like "*/api/galleries/$id") 'Incorrect Location'
    $details = Send-Api GET "/api/galleries/$id"
    Assert-True ($details.Status -eq 200 -and $details.Data.expiresAt -eq $created.Data.expiresAt) 'Create/read round trip failed'
    $list = Send-Api GET '/api/galleries'
    Assert-True ($list.Status -eq 200 -and $id -in $list.Data.galleryId) 'Created Gallery absent from list'
    Assert-True ('00000000-0000-0000-0000-000000000199' -notin $list.Data.galleryId) 'Foreign Gallery leaked'
    $sorted = @($list.Data | Sort-Object -Property @{ Expression = { $_.createdAt }; Descending = $true }, galleryId)
    Assert-True (($sorted.galleryId -join ',') -eq ($list.Data.galleryId -join ',')) 'Incorrect list order'

    foreach ($body in @('{}', '{', '{"name":"x","expiresAt":"завтра"}',
        '{"name":"x","expiresAt":"2030-01-01T12:00:00"}',
        '{"name":"x","expiresAt":null}',
        (@{ name = ' '; expiresAt = $future } | ConvertTo-Json),
        (@{ name = ('a' * 101); expiresAt = $future } | ConvertTo-Json))) {
        $response = Send-Api POST '/api/galleries' $body
        Assert-Problem $response 400 'VALIDATION_FAILED'
        Assert-True ($null -ne $response.Data.errors) 'Validation errors missing'
    }
    Assert-Problem (Send-Api GET '/api/galleries/invalid-uuid') 400 'VALIDATION_FAILED'
    $offsetDate = [DateTimeOffset]::UtcNow.AddDays(1).ToOffset([TimeSpan]::FromHours(3))
    $boundary = Send-Api POST '/api/galleries' (@{ name = ('a' * 100); expiresAt = $offsetDate.ToString('O') } | ConvertTo-Json)
    try {
        Assert-True ($boundary.Status -eq 201 -and $boundary.Data.name.Length -eq 100) '100-character name must be accepted'
        $wireDate = [regex]::Match($boundary.Text, '"expiresAt":"([^"]+)"').Groups[1].Value
        Assert-True ([DateTimeOffset]::Parse($wireDate).Offset -eq [TimeSpan]::Zero) 'expiresAt must be stored in UTC'
        Assert-True ([DateTimeOffset]::Parse($wireDate) -eq $offsetDate) 'UTC normalization changed the instant'
    } finally { $null = Send-Api DELETE "/api/galleries/$($boundary.Data.galleryId)" }
    Assert-Problem (Send-Api POST '/api/galleries' 'text' 'text/plain') 415 'UNSUPPORTED_MEDIA_TYPE'
    Assert-Problem (Send-Api POST '/api/galleries?demoError=415' $payload) 415 'UNSUPPORTED_MEDIA_TYPE'
    foreach ($date in @('2020-01-01T00:00:00Z', [DateTimeOffset]::UtcNow.AddDays(31).ToString('O'))) {
        $response = Send-Api POST '/api/galleries' (@{ name = 'x'; expiresAt = $date } | ConvertTo-Json)
        Assert-Problem $response 422 'GALLERY_EXPIRES_AT_INVALID'
        Assert-True ($null -ne $response.Data.errors.expiresAt) 'expiresAt error missing'
    }

    foreach ($method in @('GET', 'POST', 'DELETE')) {
        $suffix = if ($method -eq 'POST') { '/finalize' } else { '' }
        $foreign = Send-Api $method "/api/galleries/00000000-0000-0000-0000-000000000199$suffix"
        $missing = Send-Api $method "/api/galleries/00000000-0000-0000-0000-000000000999$suffix"
        Assert-Problem $foreign 404 'GALLERY_NOT_FOUND'
        Assert-Problem $missing 404 'GALLERY_NOT_FOUND'
        Assert-True ($foreign.Data.detail -eq $missing.Data.detail -and $foreign.Data.title -eq $missing.Data.title) '404 reveals ownership'
    }
    foreach ($number in @(101, 102, 106)) {
        Assert-Problem (Send-Api POST "/api/galleries/00000000-0000-0000-0000-000000000$number/finalize") 409 'GALLERY_NOT_FINALIZABLE'
    }
    $expired = Send-Api GET '/api/galleries/00000000-0000-0000-0000-000000000106'
    Assert-True ($expired.Data.status -eq 'Expired') 'Expired metadata not available'
    foreach ($number in @(103, 104, 105)) {
        $first = Send-Api POST "/api/galleries/00000000-0000-0000-0000-000000000$number/finalize"
        $again = Send-Api POST "/api/galleries/00000000-0000-0000-0000-000000000$number/finalize"
        Assert-True ($first.Status -eq 200 -and $again.Status -eq 200) 'Finalize must be idempotent'
        Assert-True ($again.Data.gallery.status -eq 'Finalized' -and $again.Data.message -eq 'Галерею вже фіналізовано') 'Incorrect repeated finalize response'
    }
    foreach ($case in @(@(401, 'UNAUTHENTICATED'), @(403, 'GALLERY_ACTION_FORBIDDEN'), @(429, 'RATE_LIMIT_EXCEEDED'), @(500, 'INTERNAL_ERROR'))) {
        $response = Send-Api GET "/api/galleries?demoError=$($case[0])"
        Assert-Problem $response $case[0] $case[1]
        if ($case[0] -eq 429) { Assert-True ([string]$response.Headers['Retry-After'] -eq '30') 'Retry-After missing' }
    }
    Assert-Problem (Send-Api GET '/api/galleries?demoError=unknown') 400 'VALIDATION_FAILED'

    $schema = (Send-Api GET '/swagger/v1/swagger.json').Data
    $operations = @($schema.paths.'/api/galleries'.get, $schema.paths.'/api/galleries'.post,
        $schema.paths.'/api/galleries/{id}'.get, $schema.paths.'/api/galleries/{id}'.delete,
        $schema.paths.'/api/galleries/{id}/finalize'.post)
    foreach ($operation in $operations) {
        Assert-True ($null -ne $operation) 'Endpoint missing in OpenAPI'
        foreach ($status in @('400', '401', '403', '429', '500')) {
            Assert-True ($null -ne $operation.responses.$status.content.'application/problem+json') "OpenAPI missing $status"
        }
    }
    Assert-True ($null -ne $schema.components.schemas.GalleryProblemDetails.properties.errorCode) 'OpenAPI missing errorCode schema'
    Assert-True ($null -ne $schema.components.schemas.GalleryProblemDetails.properties.traceId) 'OpenAPI missing traceId schema'
    Assert-True ($null -ne $schema.paths.'/api/galleries'.post.responses.'201') 'Create response missing in OpenAPI'
}
finally {
    $deleted = Send-Api DELETE "/api/galleries/$id"
    Assert-True ($deleted.Status -eq 204 -and -not $deleted.Text) 'Delete must return empty 204'
}
Assert-Problem (Send-Api GET "/api/galleries/$id") 404 'GALLERY_NOT_FOUND'
Assert-Problem (Send-Api DELETE "/api/galleries/$id") 404 'GALLERY_NOT_FOUND'
Assert-True ($id -notin (Send-Api GET '/api/galleries').Data.galleryId) 'Deleted Gallery remains in list'
Write-Output "Zone A smoke checks passed: $checks"
