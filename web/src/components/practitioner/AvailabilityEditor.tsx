"use client";
import { useState } from "react";
import { Alert, Button, Card, Field, Select, TextInput } from "@/components/ui";
import { createApi, type PractitionerApi } from "./api";
import { TIMES, issuesFromServer, validateAvailability, type Availability, type Issue, type Rule } from "./availability";
import { generalMessage } from "./fieldErrors";
import { pp, ppl } from "./strings";

const DAYS = ppl.availability.days;
const NEW_RULE: Rule = { isoWeekday: 1, startLocal: "09:00", endLocal: "10:00" };

function message(i: Issue): string {
  const text = pp(`availability.issues.${i.code}`, { day: i.weekday ? DAYS[i.weekday - 1] : "" });
  return i.index === undefined ? text : pp("availability.rowIssue", { n: i.index + 1, message: text });
}

/** Weekly windows by weekday on :00 and :30, and blocked dates. Local time in Europe/Warsaw; the server converts it (CONTRACTS 9.4). */
export function AvailabilityEditor({ initial, api }: { initial: Availability; api?: PractitionerApi }) {
  const [rules, setRules] = useState(initial.rules);
  const [days, setDays] = useState(initial.exceptions.map((e) => e.dateLocal));
  const [date, setDate] = useState("");
  const [server, setServer] = useState<Issue[]>([]);
  const [general, setGeneral] = useState<string>();
  const [done, setDone] = useState(false);
  const [pending, setPending] = useState(false);
  const draft = (): Availability => ({ rules, exceptions: days.map((dateLocal) => ({ dateLocal })) });
  const issues = [...validateAvailability(draft()), ...server];
  const touch = () => { setServer([]); setGeneral(undefined); setDone(false); };
  const patch = (i: number, p: Partial<Rule>) => { setRules((r) => r.map((x, j) => (j === i ? { ...x, ...p } : x))); touch(); };

  async function save() {
    if (pending || validateAvailability(draft()).length > 0) return; // the issues are already on screen
    setPending(true);
    touch();
    const r = await (api ?? createApi()).saveAvailability(draft());
    setPending(false);
    if (r.ok) {
      setRules(r.value.rules);
      setDays(r.value.exceptions.map((e) => e.dateLocal));
      return setDone(true);
    }
    const mapped = issuesFromServer(r.problem.errors);
    setServer(mapped);
    if (mapped.length === 0) setGeneral(generalMessage(r.problem, pp("availability.failed")));
  }

  return (
    <>
      <h1>{pp("availability.title")}</h1>
      <p>{pp("availability.intro")}</p>
      {done ? <Alert variant="success">{pp("common.saved")}</Alert> : null}
      {general ? <Alert variant="danger">{general}</Alert> : null}
      {issues.length > 0 ? (
        <Alert variant="danger">
          <ul>{issues.map((i, k) => <li key={k}>{message(i)}</li>)}</ul>
        </Alert>
      ) : null}
      <Card>
        <h2>{pp("availability.weekly")}</h2>
        {rules.length === 0 ? <p>{pp("availability.noRules")}</p> : null}
        {rules.map((r, i) => (
          <div key={i} role="group" aria-label={pp("availability.rowIssue", { n: i + 1, message: "" })}>
            <Field label={pp("availability.day")}>
              {(c) => (
                <Select {...c} value={r.isoWeekday} onChange={(e) => patch(i, { isoWeekday: Number(e.target.value) })}>
                  {DAYS.map((d, k) => <option key={d} value={k + 1}>{d}</option>)}
                </Select>
              )}
            </Field>
            <Field label={pp("availability.from")}>
              {(c) => <Select {...c} value={r.startLocal} onChange={(e) => patch(i, { startLocal: e.target.value })}>{TIMES.map((t) => <option key={t}>{t}</option>)}</Select>}
            </Field>
            <Field label={pp("availability.to")}>
              {(c) => <Select {...c} value={r.endLocal} onChange={(e) => patch(i, { endLocal: e.target.value })}>{TIMES.map((t) => <option key={t}>{t}</option>)}</Select>}
            </Field>
            <Button variant="secondary" onClick={() => { setRules((x) => x.filter((_, j) => j !== i)); touch(); }}>{pp("common.remove")}</Button>
          </div>
        ))}
        <Button variant="secondary" onClick={() => { setRules((x) => [...x, { ...NEW_RULE }]); touch(); }}>{pp("availability.addRule")}</Button>
      </Card>
      <Card>
        <h2>{pp("availability.exceptions")}</h2>
        {days.length === 0 ? <p>{pp("availability.noExceptions")}</p> : (
          <ul>
            {days.map((d, i) => (
              <li key={i}>{d} <Button variant="secondary" onClick={() => { setDays((x) => x.filter((_, j) => j !== i)); touch(); }}>{pp("common.remove")}</Button></li>
            ))}
          </ul>
        )}
        <Field label={pp("availability.exceptionDate")}>
          {(c) => <TextInput {...c} type="date" value={date} onChange={(e) => setDate(e.target.value)} />}
        </Field>
        <Button variant="secondary" onClick={() => { if (date) { setDays((x) => [...x, date].sort()); setDate(""); touch(); } }}>{pp("availability.addException")}</Button>
      </Card>
      <Button onClick={save} loading={pending}>{pending ? pp("common.saving") : pp("common.save")}</Button>
    </>
  );
}
