-- AlterTable
ALTER TABLE "Deal" ADD COLUMN     "aiScore" INTEGER,
ADD COLUMN     "aiScoreRationale" TEXT,
ADD COLUMN     "aiScoreSignals" JSONB,
ADD COLUMN     "aiScoredAt" TIMESTAMP(3);
