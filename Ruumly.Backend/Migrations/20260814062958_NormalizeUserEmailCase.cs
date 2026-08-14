using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeUserEmailCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Users.Email is the login identity, and mail is delivered
            // case-insensitively — "Info@Yard.ee" and "info@yard.ee" are one mailbox.
            // IX_Users_Email is unique on the RAW column with no collation override,
            // so both could persist as separate accounts. Two rows that LOWER-match
            // then make LoginAsync's `u.Email.ToLower() == ...` lookup ambiguous (no
            // ORDER BY — the row you get is whatever the plan happens to return), and
            // they 409 the claim → account flow for a provider who is entitled to it.
            //
            // Step 1 fixes the stored casing. Deliberately scoped to rows with NO
            // case-twin, which makes it incapable of violating either the existing
            // unique index or the one created below: a row is only lowercased when no
            // other row already occupies that lowercased address. Idempotent.
            migrationBuilder.Sql(@"
                UPDATE ""Users"" u
                SET    ""Email"" = LOWER(u.""Email"")
                WHERE  u.""Email"" <> LOWER(u.""Email"")
                  AND  NOT EXISTS (
                           SELECT 1
                           FROM   ""Users"" o
                           WHERE  o.""Id"" <> u.""Id""
                             AND  LOWER(o.""Email"") = LOWER(u.""Email""));
            ");

            // Step 2 makes the database enforce it, so a race between two concurrent
            // signups cannot reintroduce a pair that the application code now prevents.
            //
            // Genuine collisions survive step 1 untouched and ON PURPOSE. Rows that
            // LOWER-match are two accounts for one person: one may hold bookings,
            // payouts or bank details, and picking a winner — or renaming a login —
            // is a support decision, not something a deploy should do silently at
            // 3am. So the index is best-effort: if any collision remains, this logs
            // and moves on rather than throwing, because Railway auto-migrates on
            // deploy and a throwing migration takes the whole API down. The EXCEPTION
            // arm covers the same outcome arriving via a concurrent insert during the
            // deploy window; PL/pgSQL runs it in a subtransaction, so the surrounding
            // migration transaction survives.
            //
            // To find out whether the index exists and what is blocking it:
            //   SELECT to_regclass('"IX_Users_Email_Lower"');
            //   SELECT LOWER("Email") AS addr, count(*), array_agg("Id")
            //   FROM "Users" GROUP BY 1 HAVING count(*) > 1;
            // Merge each pair by hand (keep the row with real history, repoint
            // SupplierId/bookings, retire the other), then create the index:
            //   CREATE UNIQUE INDEX CONCURRENTLY "IX_Users_Email_Lower"
            //   ON "Users" (LOWER("Email"));
            //
            // The application does not depend on the index existing: every path that
            // writes Users.Email now stores EmailValidation.Normalize'd text and every
            // lookup compares case-insensitively. This is the backstop, not the fix.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    duplicated int;
                BEGIN
                    SELECT count(*) INTO duplicated
                    FROM (
                        SELECT LOWER(""Email"")
                        FROM   ""Users""
                        GROUP  BY 1
                        HAVING count(*) > 1
                    ) d;

                    IF duplicated > 0 THEN
                        RAISE WARNING
                            'IX_Users_Email_Lower NOT created: % address(es) exist as case-duplicate Users rows. Merge them by hand, then create the index. Logins for those addresses stay ambiguous until you do.',
                            duplicated;
                    ELSE
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Users_Email_Lower""
                            ON ""Users"" (LOWER(""Email""));
                    END IF;
                EXCEPTION WHEN unique_violation THEN
                    RAISE WARNING
                        'IX_Users_Email_Lower NOT created: a case-duplicate Users row appeared while the index was being built.';
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Users_Email_Lower"";");

            // The casing normalization has no meaningful inverse — the original
            // capitalisation was never load-bearing, and re-introducing it would only
            // recreate the ambiguity this migration removed.
        }
    }
}
