import { env } from "cloudflare:workers";
import { drizzle } from "drizzle-orm/d1";
import * as schema from "./schema";

// The public landing page does not provision D1. Keep the optional binding
// explicit so the same code type-checks both before and after a database is
// enabled in .openai/hosting.json.
const bindings = env as typeof env & { DB?: D1Database };

export function getDb() {
  if (!bindings.DB) {
    throw new Error(
      "Cloudflare D1 binding `DB` is unavailable. Set the `d1` field in .openai/hosting.json to `DB` or let your control plane inject the real binding values before using the database."
    );
  }

  return drizzle(bindings.DB, { schema });
}
