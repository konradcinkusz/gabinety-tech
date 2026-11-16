// All server-to-backend traffic goes through this function so tests can replace it.
type Fetch = (input: string | URL, init?: RequestInit) => Promise<Response>;
const real: Fetch = (input, init) => fetch(input, init);
let impl: Fetch = real;

export const backendFetch: Fetch = (input, init) => impl(input, init);
export const setBackendFetch = (f: Fetch) => {
  impl = f;
};
export const resetBackendFetch = () => {
  impl = real;
};
