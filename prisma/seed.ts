import "dotenv/config";
import bcrypt from "bcryptjs";
import { db } from "../src/lib/db";

const DEV_EMAIL = "demo@example.com";
const DEV_PASSWORD = "password123";

async function main() {
  const passwordHash = await bcrypt.hash(DEV_PASSWORD, 10);

  const user = await db.user.upsert({
    where: { email: DEV_EMAIL },
    update: {},
    create: { email: DEV_EMAIL, name: "Demo Rep", passwordHash },
  });

  const workspace = await db.workspace.upsert({
    where: { id: "demo-workspace" },
    update: {},
    create: {
      id: "demo-workspace",
      name: "Acme Sales",
      defaultCurrency: "USD",
    },
  });

  await db.workspaceMember.upsert({
    where: { workspaceId_userId: { workspaceId: workspace.id, userId: user.id } },
    update: {},
    create: { workspaceId: workspace.id, userId: user.id, role: "owner" },
  });

  const pipeline = await db.pipeline.upsert({
    where: { id: "demo-pipeline" },
    update: {},
    create: {
      id: "demo-pipeline",
      workspaceId: workspace.id,
      name: "New Business",
      isDefault: true,
    },
  });

  const stageDefs = [
    { id: "demo-stage-prospecting", name: "Prospecting", order: 0, probability: 10 },
    { id: "demo-stage-qualified", name: "Qualified", order: 1, probability: 30 },
    { id: "demo-stage-proposal", name: "Proposal", order: 2, probability: 60 },
    { id: "demo-stage-negotiation", name: "Negotiation", order: 3, probability: 80 },
    { id: "demo-stage-won", name: "Closed Won", order: 4, probability: 100, isWon: true },
    { id: "demo-stage-lost", name: "Closed Lost", order: 5, probability: 0, isLost: true },
  ];

  for (const stage of stageDefs) {
    await db.stage.upsert({
      where: { id: stage.id },
      update: {},
      create: { ...stage, pipelineId: pipeline.id },
    });
  }

  const company = await db.company.upsert({
    where: { id: "demo-company" },
    update: {},
    create: {
      id: "demo-company",
      workspaceId: workspace.id,
      name: "Globex Corporation",
      domain: "globex.example",
    },
  });

  const contact = await db.contact.upsert({
    where: { id: "demo-contact" },
    update: {},
    create: {
      id: "demo-contact",
      workspaceId: workspace.id,
      companyId: company.id,
      firstName: "Jane",
      lastName: "Doe",
      email: "jane.doe@globex.example",
      lifecycleStage: "opportunity",
    },
  });

  await db.deal.upsert({
    where: { id: "demo-deal" },
    update: {},
    create: {
      id: "demo-deal",
      workspaceId: workspace.id,
      pipelineId: pipeline.id,
      stageId: "demo-stage-qualified",
      companyId: company.id,
      amountCents: 4_500_000,
      currency: "USD",
      contacts: { connect: [{ id: contact.id }] },
    },
  });

  const activityDefs = [
    {
      id: "demo-activity-1",
      type: "call",
      body: "Intro call with Jane. Interested in the Enterprise tier, wants a security review before signing.",
    },
    {
      id: "demo-activity-2",
      type: "email",
      body: "Sent pricing breakdown and a comparison against their current vendor.",
    },
  ];
  for (const activity of activityDefs) {
    await db.activity.upsert({
      where: { id: activity.id },
      update: {},
      create: {
        id: activity.id,
        workspaceId: workspace.id,
        dealId: "demo-deal",
        companyId: company.id,
        type: activity.type,
        body: activity.body,
      },
    });
  }

  console.log(`Seeded. Sign in with ${DEV_EMAIL} / ${DEV_PASSWORD}`);
}

main()
  .catch((err) => {
    console.error(err);
    process.exitCode = 1;
  })
  .finally(async () => {
    await db.$disconnect();
  });
