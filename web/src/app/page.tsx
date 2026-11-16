import { pl } from "@/i18n/pl";

export default function Home() {
  return (
    // The root layout already provides <main id="main">: a second main landmark here would nest one in the other.
    <>
      <h1>{pl.site.name}</h1>
      <p>{pl.site.tagline}</p>
    </>
  );
}
