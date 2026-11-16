export function problem(status: number, code: string, headers?: HeadersInit): Response {
  return new Response(JSON.stringify({ type: `urn:gabinety:error:${code}`, code, status }), {
    status,
    headers: { "Content-Type": "application/problem+json", "Cache-Control": "no-store", ...headers },
  });
}
export const dependencyUnavailable = () => problem(503, "dependency_unavailable");
