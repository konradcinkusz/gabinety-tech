import { Alert, Card, LinkButton } from "@/components/ui";
import type { Account } from "./account.server";
import { AccountActions, type AccountActionHandlers } from "./AccountActions";
import { at } from "./strings";

export function AccountView({ account, actions, failed }: { account: Account; actions: AccountActionHandlers; failed?: boolean }) {
  const pending = !account.terms.accepted || !account.privacy.accepted;
  return (
    <>
      <h1>{at("account.title")}</h1>
      <Card>
        <dl>
          <dt>{at("account.email")}</dt>
          <dd>{account.email}</dd>
        </dl>
        <Alert variant={account.emailConfirmed ? "success" : "warning"}>
          {account.emailConfirmed ? at("account.verified") : at("account.notVerified")}
        </Alert>
      </Card>
      <Card>
        <h2>{at("account.consents")}</h2>
        <ul>
          <li>{at(account.terms.accepted ? "account.termsAccepted" : "account.termsPending", { version: account.terms.version })}</li>
          <li>{at(account.privacy.accepted ? "account.privacyAccepted" : "account.privacyPending", { version: account.privacy.version })}</li>
        </ul>
      </Card>
      <Card>
        <h2>{at("account.data")}</h2>
        <LinkButton href="/konto/export" variant="secondary">{at("account.export")}</LinkButton>
      </Card>
      {failed ? <Alert variant="danger">{at("account.deleteFailed")}</Alert> : null}
      <AccountActions actions={actions} hasPassword={account.hasPassword} consentPending={pending} />
    </>
  );
}
