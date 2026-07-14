---
name: crm-data-model
description: Canonical Prisma schema reference for this CRM's core entities (Workspace, Contact, Company, Deal, Pipeline, Stage, Activity, Task, WorkspaceMember). Load this before creating or modifying the Prisma schema, adding a new CRM entity, or writing a migration, so new tables follow the same multi-tenancy, soft-delete, and naming conventions as everything else instead of drifting.
---

# CRM data model

Reference schema for this project's core entities. Treat this as the source of
truth for naming and relations; extend it rather than inventing parallel
structures. See the `crm-domain-expert` agent for *why* the model is shaped this
way, and `database-schema-expert` for indexing/RLS/migration conventions.

## Canonical Prisma shape

```prisma
model Workspace {
  id              String   @id @default(cuid())
  name            String
  defaultCurrency String   @default("USD") // ISO 4217; see i18n-currency-timezone skill
  createdAt       DateTime @default(now())
  updatedAt       DateTime @updatedAt

  members   WorkspaceMember[]
  contacts  Contact[]
  companies Company[]
  deals     Deal[]
  pipelines Pipeline[]
}

model WorkspaceMember {
  id          String   @id @default(cuid())
  workspaceId String
  userId      String
  role        String   // "owner" | "admin" | "member"
  timezone    String   @default("UTC") // IANA tz, e.g. "America/New_York" — see i18n-currency-timezone
  createdAt   DateTime @default(now())

  workspace Workspace @relation(fields: [workspaceId], references: [id])

  @@unique([workspaceId, userId])
}

model Contact {
  id             String    @id @default(cuid())
  workspaceId    String
  companyId      String?
  firstName      String?
  lastName       String?
  email          String?
  phone          String?
  lifecycleStage String    @default("lead") // subscriber|lead|mql|sql|opportunity|customer|churned
  customFields   Json?
  createdAt      DateTime  @default(now())
  updatedAt      DateTime  @updatedAt
  deletedAt      DateTime?

  workspace  Workspace   @relation(fields: [workspaceId], references: [id])
  company    Company?    @relation(fields: [companyId], references: [id])
  deals      Deal[]
  activities Activity[]
  tasks      Task[]

  @@index([workspaceId, companyId])
  @@index([workspaceId, email])
}

model Company {
  id          String    @id @default(cuid())
  workspaceId String
  name        String
  domain      String?
  customFields Json?
  createdAt   DateTime  @default(now())
  updatedAt   DateTime  @updatedAt
  deletedAt   DateTime?

  workspace Workspace @relation(fields: [workspaceId], references: [id])
  contacts  Contact[]
  deals     Deal[]

  @@index([workspaceId, name])
}

model Pipeline {
  id          String   @id @default(cuid())
  workspaceId String
  name        String
  isDefault   Boolean  @default(false)

  workspace Workspace @relation(fields: [workspaceId], references: [id])
  stages    Stage[]
  deals     Deal[]
}

model Stage {
  id          String  @id @default(cuid())
  pipelineId  String
  name        String
  order       Int
  probability Int     // 0-100
  isWon       Boolean @default(false)
  isLost      Boolean @default(false)

  pipeline Pipeline @relation(fields: [pipelineId], references: [id])
  deals    Deal[]

  @@index([pipelineId, order])
}

model Deal {
  id               String    @id @default(cuid())
  workspaceId      String
  pipelineId       String
  stageId          String
  companyId        String?
  amountCents      Int?
  currency         String?   // ISO 4217; falls back to Workspace.defaultCurrency when unset
  forecastCategory String    @default("pipeline") // pipeline|best_case|commit|closed
  closedAt         DateTime?
  createdAt        DateTime  @default(now())
  updatedAt        DateTime  @updatedAt
  deletedAt        DateTime?

  workspace  Workspace  @relation(fields: [workspaceId], references: [id])
  pipeline   Pipeline   @relation(fields: [pipelineId], references: [id])
  stage      Stage      @relation(fields: [stageId], references: [id])
  company    Company?   @relation(fields: [companyId], references: [id])
  contacts   Contact[]
  activities Activity[]
  tasks      Task[]

  @@index([workspaceId, stageId])
  @@index([workspaceId, pipelineId])
}

model Activity {
  id          String   @id @default(cuid())
  workspaceId String
  type        String   // call|email|meeting|note
  body        String?
  contactId   String?
  companyId   String?
  dealId      String?
  createdAt   DateTime @default(now())

  contact Contact? @relation(fields: [contactId], references: [id])
  company Company? @relation(fields: [companyId], references: [id])
  deal    Deal?    @relation(fields: [dealId], references: [id])

  @@index([workspaceId, dealId, createdAt])
  @@index([workspaceId, contactId, createdAt])
}

model Task {
  id           String    @id @default(cuid())
  workspaceId  String
  title        String
  dueAt        DateTime?
  completedAt  DateTime?
  aiSuggested  Boolean   @default(false)
  contactId    String?
  companyId    String?
  dealId       String?
  createdAt    DateTime  @default(now())

  contact Contact? @relation(fields: [contactId], references: [id])
  deal    Deal?    @relation(fields: [dealId], references: [id])

  @@index([workspaceId, dueAt])
}

// -- Workspace configuration (what makes the same schema work across
// -- verticals — see the `workspace-customization` skill for the full pattern)

model WorkspaceSettings {
  id              String @id @default(cuid())
  workspaceId     String @unique
  terminology     Json   @default("{}") // entity/field label overrides
  enabledModules  String[] @default([]) // e.g. ["listings", "policies"]

  workspace Workspace @relation(fields: [workspaceId], references: [id])
}

model FieldDefinition {
  id          String   @id @default(cuid())
  workspaceId String
  entityType  String   // "contact" | "company" | "deal"
  key         String   // stable key used in that entity's customFields Json
  label       String   // human-facing label, shown in forms/tables/AI context
  fieldType   String   // "text" | "number" | "select" | "date" | "boolean"
  options     Json?    // for "select": array of allowed values
  required    Boolean  @default(false)
  order       Int      @default(0)

  workspace Workspace @relation(fields: [workspaceId], references: [id])

  @@unique([workspaceId, entityType, key])
  @@index([workspaceId, entityType, order])
}
```

## Rules when extending this model

- Every new tenant-scoped table gets `workspaceId` plus a composite index pairing
  it with whatever the table is commonly filtered/sorted by — never a bare index
  on `workspaceId` alone.
- User-facing records get `deletedAt` soft delete; join/log tables don't need it.
- Polymorphic attachment (Activity/Task → Contact/Company/Deal) uses explicit
  nullable FK columns, not a generic `entityType`/`entityId` pair.
- Money is `Int` cents, never `Float`.
- A new vertical-specific requirement is **not** a reason to add a column to
  `Contact`/`Company`/`Deal` — it's a `FieldDefinition` row (or, if the data
  shape is genuinely different, an optional module table). See
  `workspace-customization` for the decision process and how terminology,
  custom fields, and modules fit together end to end.
- This file covers the core transactional entities only. Reporting read-model
  tables, `Notification`/`NotificationPreference`, and `ApiKey`/
  `WebhookSubscription`/`WebhookDelivery` live in their own skills
  (`reporting-read-models`, `notifications-and-digests`,
  `public-api-and-webhooks`) rather than here, since they're owned by different
  agents and have different lifecycle/consistency requirements than the core
  model — don't merge them into this file.
