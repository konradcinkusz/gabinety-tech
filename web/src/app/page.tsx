import { pl } from "@/i18n/pl";

export default function Home() {
  return (
    <main>
      <h1>{pl.site.name}</h1>
      <p>{pl.site.tagline}</p>
    </main>
  );
}
