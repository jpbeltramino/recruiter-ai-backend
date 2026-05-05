# RecruiterAI

Plataforma de reclutamiento inteligente con IA. Permite analizar CVs, detectar inconsistencias, generar preguntas de entrevista y evaluar perfiles de LinkedIn usando Claude como motor de IA.

## Stack

- **Backend**: ASP.NET Core 8, C#, Anthropic.SDK 4.7.2, iText7 8.x
- **Frontend**: HTML/JS/CSS vanilla (listo para migrar a Angular 17)
- **IA**: Claude claude-haiku-4-5-20251001 (rápido y bajo costo)

## Estructura

```
RecruiterAI/
├── Controllers/
│   └── RecruitController.cs      # 4 endpoints REST
├── Models/
│   └── RecruitModels.cs          # Records de request/response
├── Services/
│   ├── ClaudeService.cs          # Integración con Anthropic SDK
│   └── CvParserService.cs        # Extracción de texto de PDFs
├── wwwroot/
│   └── index.html                # Frontend vanilla (single-file)
├── Program.cs                    # Middleware, CORS, error handling
├── RecruiterAI.csproj
├── appsettings.json
└── README.md
```

## Setup rápido

### 1. Clonar y configurar la API key

```bash
# Editar appsettings.json y reemplazar el placeholder:
"Anthropic": {
  "ApiKey": "sk-ant-..."
}
```

### 2. Restaurar dependencias y ejecutar

```bash
cd RecruiterAI
dotnet restore
dotnet run
```

El servidor arranca en `http://localhost:5000` (o el puerto que muestre la consola).
Abrí el browser en esa URL — el frontend se sirve automáticamente desde `wwwroot/`.

### 3. (Opcional) Compilar en modo release

```bash
dotnet publish -c Release -o ./publish
cd publish
./RecruiterAI
```

## Endpoints de la API

| Método | URL | Descripción |
|--------|-----|-------------|
| POST | `/api/recruit/rank-cvs` | Rankea varios CVs contra el puesto |
| POST | `/api/recruit/detect-inconsistencies` | Detecta inconsistencias en un CV |
| POST | `/api/recruit/generate-questions` | Genera preguntas de entrevista |
| POST | `/api/recruit/analyze-linkedin` | Analiza perfil de LinkedIn |

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

### Ejemplo: detect-inconsistencies

```json
POST /api/recruit/detect-inconsistencies
{
  "cvText": "Texto del CV...",
  "pdfBase64": null
}
```

### Ejemplo: generate-questions

```json
POST /api/recruit/generate-questions
{
  "jobDescription": "...",
  "cvText": "...",
  "pdfBase64": null
}
```

### Ejemplo: analyze-linkedin

```json
POST /api/recruit/analyze-linkedin
{
  "jobDescription": "...",
  "profileText": "Texto del perfil copiado de LinkedIn..."
}
```

## Decisiones técnicas

### Por qué no se usa `SystemMessage` ni `System` en Anthropic.SDK 4.x

La versión 4.x del SDK eliminó la propiedad `System` de `MessageParameters`. Las instrucciones del sistema se embeben directamente en el user message usando XML tags:

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

El middleware en `Program.cs` captura todas las excepciones no manejadas y las devuelve como `{ "error": "..." }`. El frontend usa `safeJson()` que lee el body como texto antes de parsear, evitando errores de parseo cuando el servidor devuelve HTML (ej: páginas de error de IIS).

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
│   └── linkedin-analyzer/
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

- [ ] Autenticación con JWT (roles: admin, recruiter)
- [ ] Base de datos para guardar análisis y candidatos
- [ ] Exportar resultados a PDF/Excel
- [ ] Comparación histórica de candidatos
- [ ] Integración con ATS (Greenhouse, Lever)
- [ ] Webhook para notificaciones
- [ ] Rate limiting por usuario
- [ ] Modo batch para procesar muchos CVs
