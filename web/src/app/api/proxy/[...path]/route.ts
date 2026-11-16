import { handleProxy } from "../../../../lib/api/proxy";

export const dynamic = "force-dynamic";

type Ctx = { params: Promise<{ path?: string[] }> };
const handler = async (request: Request, ctx: Ctx) => handleProxy(request, (await ctx.params).path ?? []);
export const GET = handler;
export const HEAD = handler;
export const POST = handler;
export const PUT = handler;
export const PATCH = handler;
export const DELETE = handler;
