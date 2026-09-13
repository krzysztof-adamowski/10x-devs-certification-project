-- FR-013: the three rates the product owner reads. Read-only; this script never writes.
--
-- Run it per TenExCards/AGENTS.md "### Reading the outcome rates" -- the password goes through
-- SQLCMDPASSWORD, never through -P, because PowerShell 5.1 keeps every command line forever.
--
-- THREE THINGS THAT LOOK LIKE BREAKAGE AND ARE NOT:
--
-- 1. NULL is the answer on an empty table, not 0 and not an error. It means "no batches yet".
--    Reading a NULL here as a failed query is the hazard lessons.md records.
-- 2. Every rate UNDERCOUNTS under fault. The recorder swallows write failures by design, so that
--    measurement can never cost a learner a card. These are floors, not exact figures.
-- 3. Untriaged pools three different things and only the first is card-quality signal:
--    candidates the learner never reached, a batch open at the moment of the query, and accepts
--    whose record write was swallowed. Result set 2 removes the second. Nothing removes the third.
--
-- Sources are deliberately split, because the two PRD criteria mean different things:
--   acceptance + edit rate  <- TriageBatches, immune to card deletion ("edited before saving")
--   AI-origin share         <- Cards,         sensitive to it        ("cards in a learner's space")
-- Do not "simplify" these onto one table; doing so gives one of them the wrong answer.

SET NOCOUNT ON;

-- 1. Acceptance rate. PRD Primary, target >= 75%. Denominator is candidates SHOWN, so a candidate
--    the learner never reached counts as not-accepted.
SELECT 'acceptance (all batches)'                                       AS Metric,
       SUM(CAST(AcceptedCount AS float)) / NULLIF(SUM(CandidateCount), 0) AS AcceptanceRate,
       SUM(CandidateCount)                                              AS CandidatesShown,
       SUM(AcceptedCount)                                               AS Accepted,
       SUM(RejectedCount)                                               AS Rejected,
       SUM(CandidateCount) - SUM(AcceptedCount) - SUM(RejectedCount)    AS Untriaged
FROM   TriageBatches;

-- 2. The same rate over settled batches only. A row carries no closed state, so a batch being
--    triaged right now is indistinguishable from an abandoned one; 30 minutes is the cutoff.
--    Abandonment is one click, not a rare tab-close: the nav menu renders throughout triage and its
--    links are enhanced navigation, which never fires beforeunload. Compare 1 and 2 before reading
--    a low headline rate as a card-quality failure.
SELECT 'acceptance (settled, >30 min old)'                              AS Metric,
       SUM(CAST(AcceptedCount AS float)) / NULLIF(SUM(CandidateCount), 0) AS SettledAcceptanceRate,
       COUNT(*)                                                         AS SettledBatches,
       SUM(CandidateCount) - SUM(AcceptedCount) - SUM(RejectedCount)    AS Untriaged
FROM   TriageBatches
WHERE  OpenedAt < DATEADD(minute, -30, SYSDATETIMEOFFSET());

-- 3. AI-origin share. PRD Primary, target >= 75%. Read from Cards, so it is deliberately sensitive
--    to deletion: the criterion is about the cards in a learner's space right now.
--    Origin is the CardOrigin enum stored as int: 1 = Generated, 2 = Manual.
SELECT 'ai-origin share'                                                AS Metric,
       SUM(CASE WHEN Origin = 1 THEN 1.0 ELSE 0 END) / NULLIF(COUNT(*), 0) AS GeneratedShare,
       COUNT(*)                                                         AS CardsInSpace,
       SUM(CASE WHEN Origin = 1 THEN 1 ELSE 0 END)                      AS Generated,
       SUM(CASE WHEN Origin = 2 THEN 1 ELSE 0 END)                      AS Manual
FROM   Cards;

-- 4. Edit rate on accepted cards. PRD Secondary, target < 25%. Read from TriageBatches, not Cards:
--    the criterion is about the moment of acceptance, and deleting an edited card must not be able
--    to improve the number. A later repair via /cards is not an edit-before-saving and is not here.
SELECT 'edit rate on accepted'                                          AS Metric,
       SUM(CAST(EditedCount AS float)) / NULLIF(SUM(AcceptedCount), 0)  AS EditRate,
       SUM(EditedCount)                                                 AS Edited,
       SUM(AcceptedCount)                                               AS Accepted
FROM   TriageBatches;

-- 5. Per learner. The PRD phrases both primary criteria per learner ("a learner's space"); the
--    pooled figures above are the headline while there is effectively one learner. One heavy
--    account can hide every other experience, which is what this set is for.
SELECT b.OwnerId,
       SUM(CAST(b.AcceptedCount AS float)) / NULLIF(SUM(b.CandidateCount), 0) AS AcceptanceRate,
       SUM(CAST(b.EditedCount AS float)) / NULLIF(SUM(b.AcceptedCount), 0)    AS EditRate,
       SUM(b.CandidateCount)                                                  AS CandidatesShown,
       (SELECT COUNT(*) FROM Cards c WHERE c.OwnerId = b.OwnerId)             AS CardsInSpace,
       (SELECT SUM(CASE WHEN c.Origin = 1 THEN 1.0 ELSE 0 END) / NULLIF(COUNT(*), 0)
        FROM Cards c WHERE c.OwnerId = b.OwnerId)                             AS GeneratedShare
FROM   TriageBatches b
GROUP  BY b.OwnerId
ORDER  BY SUM(b.CandidateCount) DESC;
