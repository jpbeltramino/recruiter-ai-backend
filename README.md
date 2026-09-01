# RecruiterAI

Plataforma de gestión de reclutamiento con IA. El análisis con Claude (rankeo de CVs,
detección de inconsistencias, generación de preguntas, análisis de LinkedIn) es una
feature dentro de un flujo más grande: postulantes persistentes, posiciones, pipeline
por etapas (kanban) y CRM de clientes para agencias.

## Stack

- **Backend**: ASP.NET Core 8, C#, Anthropic.SDK 4.7.2, iText7 8.x
- **Persistencia**: PostgreSQL + EF Core 8 (Npgsql)
- **Frontend**: HTML/JS/CSS vanilla (listo para migrar a Angular 17)
- **IA**: Claude claude-haiku-4-5-20251001 (rápido y bajo costo)

## Estructura

```
RecruiterAI/
├── Controllers/
│   ├── RecruitController.cs      # 4 endpoints de análisis con IA
│   ├── ClientsController.cs      # CRM de clientes (agencias)
│   ├── PositionsController.cs    # Posiciones / vacantes
│   ├── CandidatesController.cs   # Postulantes persistentes
│   ├── PipelineController.cs     # Pipeline por etapas / kanban
│   ├── WorkspacesController.cs   # Bootstrap de workspaces (multi-tenancy)
│   └── ManagementControllerBase.cs
├── Models/
│   ├── RecruitModels.cs          # Records de request/response del análisis IA
│   ├── ManagementModels.cs       # DTOs de candidates/positions/clients/pipeline
│   └── Entities/
│       └── ManagementEntities.cs # Entidades EF Core (schema v2)
├── Data/
│   └── RecruiterAIDbContext.cs   # DbContext, mapeo 1:1 contra Database/schema.sql
├── Database/
│   ├── schema.sql                # DDL completo de Postgres (fuente de verdad)
│   └── SCHEMA.md                 # ERD + decisiones de diseño
├── Services/
│   ├── ClaudeService.cs          # Integración con Anthropic SDK
│   ├── CvParserService.cs        # Extracción de texto de PDFs
│   ├── FileValidationService.cs
│   ├── RateLimitService.cs
│   └── JobService.cs             # Jobs de análisis en background (in-memory)
├── wwwroot/
│   └── index.html                # Frontend vanilla (single-file)
├── Program.cs                    # Middleware, CORS, error handling, DbContext
├── RecruiterAI.csproj
├── appsettings.json
└── README.md
```

## Setup rápido

### 1. Base de datos PostgreSQL

Necesitás un Postgres accesible (local, Docker, o un proveedor gestionado como
Railway/Supabase/RDS). Con Docker:

```bash
docker run --name reclutaria-db -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=reclutaria -p 5432:5432 -d postgres:16
```

Aplicá el schema (todavía no usamos migraciones de EF Core — `Database/schema.sql`
es la fuente de verdad):

```bash
psql "postgresql://postgres:postgres@localhost:5432/reclutaria" -f Database/schema.sql
```

Ajustá `ConnectionStrings:Default` en `appsettings.json` si tu conexión es distinta.

### 2. API key de Anthropic

```bash
# Editar appsettings.json y reemplazar el placeholder:
"Anthropic": {
  "ApiKey": "sk-ant-..."
}
```

### 3. Restaurar dependencias y ejecutar

```bash
cd RecruiterAI
dotnet restore
dotnet run
```

El servidor arranca en `http://localhost:5000` (o el puerto que muestre la consola).
Abrí el browser en esa URL — el frontend se sirve automáticamente desde `wwwroot/`.

### 4. Crear el primer workspace

Los módulos de gestión (`candidates`, `positions`, `clients`, `pipeline`) requieren
un `workspace_id` en el header `X-Workspace-Id` de cada request. Creá uno primero:

```bash
curl -X POST http://localhost:5000/api/workspaces \
  -H "Content-Type: application/json" \
  -H "X-Api-Token: <tu token de Auth:ValidTokens>" \
  -d '{"name": "Mi agencia", "planTier": "free"}'
```

Guardá el `id` que devuelve — se dispara automáticamente el trigger que siembra las
6 etapas default del pipeline (Nuevo → Screening → Entrevista → Oferta →
Contratado/Descartado). Usalo como `X-Workspace-Id` en el resto de los endpoints.

### 5. (Opcional) Compilar en modo release

```bash
dotnet publish -c Release -o ./publish
cd publish
./RecruiterAI
```

## Endpoints de la API

### Análisis con IA (`/api/recruit`) — sin cambios

| Método | URL | Descripción |
|--------|-----|-------------|
| POST | `/api/recruit/rank-cvs` | Rankea varios CVs contra el puesto |
| POST | `/api/recruit/detect-inconsistencies` | Detecta inconsistencias en un CV |
| POST | `/api/recruit/generate-questions` | Genera preguntas de entrevista |
| POST | `/api/recruit/analyze-linkedin` | Analiza perfil de LinkedIn |
| POST | `/api/recruit/analyze-unified` | Rankeo + análisis profundo combinado |
| POST | `/api/recruit/analyze-unified-stream` | Igual, con progreso vía SSE |
| POST | `/api/recruit/analyze-job` | Igual, como job en background (polling) |
| GET | `/api/recruit/jobs/{jobId}` | Estado de un job |

### Gestión (nuevo — requieren header `X-Workspace-Id`)

| Método | URL | Descripción |
|--------|-----|-------------|
| GET/POST | `/api/workspaces` | Listar / crear workspaces |
| GET/POST | `/api/clients` | CRM de clientes |
| GET/PUT/DELETE | `/api/clients/{id}` | Detalle / editar / borrar cliente |
| GET/POST | `/api/positions` | Posiciones / vacantes |
| GET/PUT/DELETE | `/api/positions/{id}` | Detalle / editar / borrar posición |
| GET/POST | `/api/candidates` | Postulantes |
| GET/PUT/DELETE | `/api/candidates/{id}` | Detalle / editar / borrar postulante |
| POST | `/api/candidates/{id}/notes` | Agregar nota a un postulante |
| GET | `/api/pipeline/positions/{positionId}/stages` | Etapas del pipeline de una posición |
| GET | `/api/pipeline/positions/{positionId}/board` | Kanban completo de una posición |
| POST | `/api/pipeline/applications` | Postular un candidato a una posición |
| PATCH | `/api/pipeline/applications/{id}/stage` | Mover el card a otra etapa |
| GET | `/api/pipeline/applications/{id}/history` | Auditoría de cambios de etapa |

Todavía **no** hay endpoints para persistir automáticamente los resultados de
`/api/recruit/*` en `cv_analyses` / `inconsistency_reports` / etc. — eso conecta el
motor de IA con el pipeline y es el siguiente paso natural (ver Database/SCHEMA.md,
sección "lo que esto no resuelve").

### Ejemplo: rank-cvs

```json
POST /api/recruit/rank-cvs
{
  "jobDescription": "Senior React Developer con TypeScript...",
  "candidates": [
    {
      "name": "Juan Pérez",
      "text": "Texto del CV aquí...",
      "pdfBase64": null
    },
    {
      "name": "Ana García",
      "text": null,
      "pdfBase64": "JVBERi0xLjQK..."
    }
  ]
}
```

### Ejemplo: crear posición y postular un candidato

```bash
# 1. Crear posición
curl -X POST http://localhost:5000/api/positions \
  -H "X-Api-Token: <token>" -H "X-Workspace-Id: <workspaceId>" \
  -H "Content-Type: application/json" \
  -d '{"title": "Senior .NET Dev", "description": "..."}'

# 2. Crear postulante
curl -X POST http://localhost:5000/api/candidates \
  -H "X-Api-Token: <token>" -H "X-Workspace-Id: <workspaceId>" \
  -H "Content-Type: application/json" \
  -d '{"fullName": "Juan Pérez", "email": "juan@mail.com"}'

# 3. Postularlo a la posición (entra en la primera etapa del pipeline)
curl -X POST http://localhost:5000/api/pipeline/applications \
  -H "X-Api-Token: <token>" -H "X-Workspace-Id: <workspaceId>" \
  -H "Content-Type: application/json" \
  -d '{"candidateId": "<id>", "positionId": "<id>"}'

# 4. Ver el kanban
curl http://localhost:5000/api/pipeline/positions/<positionId>/board \
  -H "X-Api-Token: <token>" -H "X-Workspace-Id: <workspaceId>"
```

## Decisiones técnicas

### Por qué no se usa `SystemMessage` ni `System` en Anthropic.SDK 4.x

La versión 4.x del SDK eliminó la propiedad `System` de `MessageParameters`. Las
instrucciones del sistema se embeben directamente en el user message usando XML tags:

```
<instrucciones>
  [instrucciones para Claude]
</instrucciones>

<datos>
  [datos del usuario]
</datos>

<tarea>
  [tarea específica]
</tarea>
```

### Por qué JSON-only en los errores

El middleware en `Program.cs` captura todas las excepciones no manejadas y las
devuelve como `{ "error": "..." }`. El frontend usa `safeJson()` que lee el body
como texto antes de parsear, evitando errores de parseo cuando el servidor
devuelve HTML (ej: páginas de error de IIS).

### Por qué schema.sql en vez de migraciones de EF Core

Este entorno de desarrollo no tenía el SDK de .NET disponible para generar y
verificar una migración inicial, y una migración escrita a mano sin poder
compilarla es más riesgo que valor. `Database/schema.sql` es la fuente de verdad
del schema — se corre una vez con `psql` y el `DbContext` está mapeado 1:1 contra
esos nombres de tabla/columna. Cuando el proyecto lo justifique, correr
`dotnet ef migrations add InitialCreate` localmente para empezar a versionar el
schema con migraciones reales (usando `schema.sql` como referencia de qué debería
generar).

### Por qué `X-Workspace-Id` como header en vez de resolverlo del token

Todavía no hay autenticación real (`Auth:ValidTokens` es una lista fija en
`appsettings.json`, no usuarios). Multi-tenancy real — donde el workspace surge
del usuario autenticado vía `workspace_members`, no de un header que cualquiera
puede mandar — queda para cuando se implemente auth con JWT (ver roadmap).

---

## Roadmap: migración a Angular 17

### Fase 1 — Setup del proyecto Angular

```bash
npm install -g @angular/cli@17
ng new recruiter-ai-frontend --standalone --style=scss --routing=false
cd recruiter-ai-frontend
ng add @angular/material
```

### Fase 2 — Estructura de componentes standalone

```
src/app/
├── core/
│   └── services/
│       └── recruit.service.ts      # HttpClient + tipos
├── shared/
│   ├── components/
│   │   ├── loading/
│   │   ├── error-alert/
│   │   └── file-upload/
│   └── models/
│       └── recruit.models.ts       # Interfaces TypeScript
├── features/
│   ├── cv-ranker/
│   │   ├── cv-ranker.component.ts
│   │   └── candidate-card/
│   ├── inconsistency-detector/
│   ├── question-generator/
│   ├── linkedin-analyzer/
│   ├── candidates/                 # NUEVO — módulo de postulantes
│   ├── positions/                  # NUEVO — módulo de posiciones
│   ├── clients/                    # NUEVO — CRM de clientes
│   └── pipeline/                   # NUEVO — kanban
└── app.component.ts                # Tab navigation
```

### Fase 3 — Servicio Angular

```typescript
// core/services/recruit.service.ts
@Injectable({ providedIn: 'root' })
export class RecruitService {
  private http = inject(HttpClient);
  private API = '/api/recruit';

  rankCvs(req: RankCvsRequest) {
    return this.http.post<RankCvsResponse>(`${this.API}/rank-cvs`, req);
  }

  detectInconsistencies(req: DetectInconsistenciesRequest) {
    return this.http.post<DetectInconsistenciesResponse>(
      `${this.API}/detect-inconsistencies`, req
    );
  }

  generateQuestions(req: GenerateQuestionsRequest) {
    return this.http.post<GenerateQuestionsResponse>(
      `${this.API}/generate-questions`, req
    );
  }

  analyzeLinkedIn(req: AnalyzeLinkedInRequest) {
    return this.http.post<AnalyzeLinkedInResponse>(
      `${this.API}/analyze-linkedin`, req
    );
  }
}
```

### Fase 4 — Proxy para desarrollo

```json
// proxy.conf.json
{
  "/api": {
    "target": "http://localhost:5000",
    "secure": false
  }
}
```

```bash
ng serve --proxy-config proxy.conf.json
```

### Fase 5 — Build y publicación integrada

```bash
# Compilar Angular en wwwroot del backend
ng build --output-path ../RecruiterAI/wwwroot --base-href /

# El backend sirve el frontend automáticamente
cd ../RecruiterAI && dotnet run
```

### Mejoras futuras

- [ ] Autenticación con JWT (roles: admin, recruiter) — reemplaza `Auth:ValidTokens`
      y resuelve `workspace_id` del usuario en vez del header `X-Workspace-Id`
- [x] Base de datos para guardar análisis y candidatos — schema v2 (`Database/schema.sql`)
- [ ] Conectar `/api/recruit/*` para que persista resultados en `cv_analyses` / etc.
- [ ] Generador de avisos para LinkedIn/portales (texto para copiar/pegar, sin integración de API)
- [ ] Integración MercadoPago Suscripciones (`preapproval_plan`)
- [ ] Exportar resultados a PDF/Excel
- [ ] Comparación histórica de candidatos
- [ ] Webhook para notificaciones
- [ ] Rate limiting por usuario (hoy es por token, no por workspace)
- [ ] Modo batch para procesar muchos CVs
