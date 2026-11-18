// authservice v0.3.4 only accepts a returnUrl whose path is /oauth/callback or below (ExternalAuthController.cs:344-352),
// so the GitHub sign-in returns here, not under /api/auth. Public on purpose: the state cookie is the guard (F-05).
import { githubCallback } from "../../../lib/session/handlers";

export const dynamic = "force-dynamic";
export const GET = githubCallback;
