-- ============================================================================
-- RecruiterAI / ReclutarIA — Schema PostgreSQL v2
-- Pasa la plataforma de "analizador de CVs stateless" a un ATS persistente:
-- postulantes, posiciones, pipeline kanban, CRM de clientes (agencias),
-- multi-tenancy (workspaces) y suscripciones (MercadoPago).
--
-- Convenciones:
--   - PK: UUID (gen_random_uuid(), requiere pgcrypto)
--   - Todo lo que pertenece a una cuenta cuelga de workspace_id (multi-tenant)
--   - Resultados de IA se persisten en tablas propias (nada vive solo en
--     memoria como el AnalysisJob actual)
-- ============================================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ────────────────────────────────────────────────────────────────────────
-- 1. IDENTIDAD Y MULTI-TENANCY (punto 11)
-- ────────────────────────────────────────────────────────────────────────

CREATE TABLE users (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email           VARCHAR(255) NOT NULL UNIQUE,
    password_hash   TEXT NOT NULL,
    full_name       VARCHAR(200) NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE workspaces (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(200) NOT NULL,
    plan_tier       VARCHAR(20) NOT NULL DEFAULT 'free', -- free | solo | agencia
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Un usuario puede pertenecer a varios workspaces (ej: agencia + proyecto propio)
CREATE TABLE workspace_members (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    workspace_id    UUID NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    user_id         UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role            VARCHAR(20) NOT NULL DEFAULT 'recruiter', -- owner | admin | recruiter
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (workspace_id, user_id)
);

-- ────────────────────────────────────────────────────────────────────────
-- 2. CRM DE CLIENTES — diferenciador para agencias/headhunters (punto 5)
-- ────────────────────────────────────────────────────────────────────────

CREATE TABLE clients (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    workspace_id    UUID NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    name            VARCHAR(200) NOT NULL,
    contact_name    VARCHAR(200),
    contact_email   VARCHAR(255),
    contact_phone   VARCHAR(50),
    notes           TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ────────────────────────────────────────────────────────────────────────
-- 3. POSICIONES / VACANTES (punto 3)
-- ────────────────────────────────────────────────────────────────────────

CREATE TABLE positions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    workspace_id    UUID NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    client_id       UUID REFERENCES clients(id) ON DELETE SET NULL, -- NULL = búsqueda in-house
    title           VARCHAR(200) NOT NULL,
    description     TEXT NOT NULL,
    status          VARCHAR(20) NOT NULL DEFAULT 'abierta', -- abierta | pausada | cerrada
    created_by      UUID REFERENCES users(id),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    closed_at       TIMESTAMPTZ
);

-- ────────────────────────────────────────────────────────────────────────
-- 4. PIPELINE KANBAN (punto 4)
-- ────────────────────────────────────────────────────────────────────────

-- Etapas del pipeline. position_id NULL = plantilla default del workspace
-- (se copia/usa para todas las posiciones salvo que alguna quiera un
-- pipeline custom, en cuyo caso se le crean stages propios).
CREATE TABLE pipeline_stages (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    workspace_id    UUID NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    position_id     UUID REFERENCES positions(id) ON DELETE CASCADE,
    name            VARCHAR(100) NOT NULL,
    order_index     INT NOT NULL,
    is_terminal     BOOLEAN NOT NULL DEFAULT false, -- true en Contratado / Descartado
    color           VARCHAR(20),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (workspace_id, position_id, order_index)
);

-- Seed automático: Nuevo → Screening → Entrevista → Oferta → Contratado/Descartado
CREATE OR REPLACE FUNCTION seed_default_pipeline_stages()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO pipeline_stages (workspace_id, position_id, name, order_index, is_terminal)
    VALUES
        (NEW.id, NULL, 'Nuevo',       1, false),
        (NEW.id, NULL, 'Screening',   2, false),
        (NEW.id, NULL, 'Entrevista',  3, false),
        (NEW.id, NULL, 'Oferta',      4, false),
        (NEW.id, NULL, 'Contratado',  5, true),
        (NEW.id, NULL, 'Descartado',  6, true);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_seed_pipeline_stages
AFTER INSERT ON workspaces
FOR EACH ROW EXECUTE FUNCTION seed_default_pipeline_stages();

-- ────────────────────────────────────────────────────────────────────────
-- 5. POSTULANTES PERSISTENTES (punto 2)
-- ────────────────────────────────────────────────────────────────────────

CREATE TABLE candidates (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    workspace_id    UUID NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    full_name       VARCHAR(200) NOT NULL,
    email           VARCHAR(255),
    phone           VARCHAR(50),
    linkedin_url    VARCHAR(500),
    cv_text         TEXT,       -- último texto de CV extraído
    cv_file_url     TEXT,       -- si se guarda el PDF original en storage externo
    source          VARCHAR(50), -- manual | linkedin | portal | referido
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE candidate_notes (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_id    UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
    author_id       UUID REFERENCES users(id),
    note            TEXT NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Postulación / asignación de un candidato a una posición = el "card" del kanban
CREATE TABLE candidate_positions (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_id        UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
    position_id         UUID NOT NULL REFERENCES positions(id) ON DELETE CASCADE,
    current_stage_id    UUID NOT NULL REFERENCES pipeline_stages(id),
    applied_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (candidate_id, position_id)
);

-- Auditoría de cambios de etapa (histórico del kanban, punto 4)
CREATE TABLE candidate_stage_history (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_position_id   UUID NOT NULL REFERENCES candidate_positions(id) ON DELETE CASCADE,
    stage_id                UUID NOT NULL REFERENCES pipeline_stages(id),
    changed_by              UUID REFERENCES users(id),
    changed_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    notes                   TEXT
);

-- ────────────────────────────────────────────────────────────────────────
-- 6. RESULTADOS DE IA — persistidos (hoy viven solo en el AnalysisJob
--    en memoria de JobService). "reasoning" es lo que habilita mostrar
--    el "por qué" del score en el frontend (punto 6, IA transparente).
-- ────────────────────────────────────────────────────────────────────────

CREATE TABLE cv_analyses (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_id    UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
    position_id     UUID REFERENCES positions(id) ON DELETE SET NULL,
    score           INT NOT NULL CHECK (score BETWEEN 1 AND 10),
    strengths       JSONB NOT NULL DEFAULT '[]',
    weaknesses      JSONB NOT NULL DEFAULT '[]',
    verdict         VARCHAR(20) NOT NULL, -- AVANZAR | REVISAR | DESCARTAR
    reasoning       TEXT,       -- explicación en lenguaje natural del score
    raw_response    JSONB,      -- respuesta completa de Claude, para auditoría
    created_by      UUID REFERENCES users(id),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE inconsistency_reports (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_id    UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
    findings        JSONB NOT NULL DEFAULT '[]', -- [{category, description, risk_level, suggested_question}]
    summary         TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE interview_question_sets (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_id            UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
    position_id             UUID REFERENCES positions(id) ON DELETE SET NULL,
    technical               JSONB NOT NULL DEFAULT '[]',
    cultural                JSONB NOT NULL DEFAULT '[]',
    weakness_validation     JSONB NOT NULL DEFAULT '[]',
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE linkedin_analyses (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_id            UUID REFERENCES candidates(id) ON DELETE CASCADE, -- puede analizarse antes de crear el candidato
    position_id             UUID REFERENCES positions(id) ON DELETE SET NULL,
    profile_text            TEXT,
    alignment_level         VARCHAR(10) NOT NULL, -- ALTO | MEDIO | BAJO
    positive_signals        JSONB NOT NULL DEFAULT '[]',
    red_flags               JSONB NOT NULL DEFAULT '[]',
    screening_questions     JSONB NOT NULL DEFAULT '[]',
    recommendation          VARCHAR(20) NOT NULL, -- CONTACTAR | EVALUAR MAS | NO CONTACTAR
    recommendation_reason   TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Generador de avisos para portales (punto 7) — sin integración de API,
-- solo se persiste el texto generado para copiar/pegar.
CREATE TABLE job_ad_generations (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    position_id     UUID NOT NULL REFERENCES positions(id) ON DELETE CASCADE,
    platform        VARCHAR(50), -- linkedin | computrabajo | zonajobs | generico
    generated_text  TEXT NOT NULL,
    created_by      UUID REFERENCES users(id),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ────────────────────────────────────────────────────────────────────────
-- 7. MONETIZACIÓN — MercadoPago Suscripciones (puntos 8 y 9)
-- ────────────────────────────────────────────────────────────────────────

CREATE TABLE subscriptions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    workspace_id            UUID NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    mp_preapproval_id       VARCHAR(100) UNIQUE, -- id del preapproval_plan en MercadoPago
    plan_tier               VARCHAR(20) NOT NULL, -- free | solo | agencia
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending', -- pending | authorized | paused | cancelled
    current_period_end      TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ────────────────────────────────────────────────────────────────────────
-- ÍNDICES
-- ────────────────────────────────────────────────────────────────────────

CREATE INDEX idx_workspace_members_user            ON workspace_members(user_id);
CREATE INDEX idx_clients_workspace                 ON clients(workspace_id);
CREATE INDEX idx_positions_workspace               ON positions(workspace_id);
CREATE INDEX idx_positions_client                  ON positions(client_id);
CREATE INDEX idx_pipeline_stages_workspace         ON pipeline_stages(workspace_id);
CREATE INDEX idx_pipeline_stages_position          ON pipeline_stages(position_id);
CREATE INDEX idx_candidates_workspace              ON candidates(workspace_id);
CREATE INDEX idx_candidate_notes_candidate         ON candidate_notes(candidate_id);
CREATE INDEX idx_candidate_positions_candidate     ON candidate_positions(candidate_id);
CREATE INDEX idx_candidate_positions_position      ON candidate_positions(position_id);
CREATE INDEX idx_candidate_positions_stage         ON candidate_positions(current_stage_id);
CREATE INDEX idx_stage_history_cand_position       ON candidate_stage_history(candidate_position_id);
CREATE INDEX idx_cv_analyses_candidate             ON cv_analyses(candidate_id);
CREATE INDEX idx_cv_analyses_position              ON cv_analyses(position_id);
CREATE INDEX idx_inconsistency_reports_candidate   ON inconsistency_reports(candidate_id);
CREATE INDEX idx_interview_questions_candidate     ON interview_question_sets(candidate_id);
CREATE INDEX idx_linkedin_analyses_candidate       ON linkedin_analyses(candidate_id);
CREATE INDEX idx_job_ad_generations_position       ON job_ad_generations(position_id);
CREATE INDEX idx_subscriptions_workspace           ON subscriptions(workspace_id);
