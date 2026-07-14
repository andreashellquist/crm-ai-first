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
  id        String   @id @default(cuid())
  name      String
  createdAt DateTime @default(now())
  updatedAt DateTime @updatedAt

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
```

## Rules when extending this model

- Every new tenant-scoped table gets `workspaceId` plus a composite index pairing
  it with whatever the table is commonly filtered/sorted by — never a bare index
  on `workspaceId` alone.
- User-facing records get `deletedAt` soft delete; join/log tables don't need it.
- Polymorphic attachment (Activity/Task → Contact/Company/Deal) uses explicit
  nullable FK columns, not a generic `entityType`/`entityId` pair.
- Money is `Int` cents, never `Float`.
