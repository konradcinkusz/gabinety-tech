import { readConfig } from "@/lib/config";

export const dynamic = "force-dynamic";
export function GET() {
  return Response.json(readConfig(), { headers: { "Cache-Control": "public, max-age=5" } });
}
