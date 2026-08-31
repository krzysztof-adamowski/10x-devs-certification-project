1. The meaningful Azure commands
The clean path — what you'd run today to reproduce this from nothing:


# Resource group. Region matters more than usual here (see note below).
az group create --name rg-tenexcards-plc --location polandcentral

# The compute. --is-linux is what makes this a Linux plan; passed explicitly
# rather than trusting a CLI default that has changed before.
az appservice plan create --name asp-tenexcards-linux -g rg-tenexcards-plc \
  --location polandcentral --is-linux --sku B1 --number-of-workers 1

# The app. --runtime takes the colon form and stores the pipe form.
# --https-only is the platform-level 301; it replaces app-level HTTPS config.
az webapp create --name tenexcards-ka -g rg-tenexcards-plc \
  --plan asp-tenexcards-linux --runtime "DOTNETCORE:10.0" --https-only true

# Hardening. always-on keeps the app resident (B1+ only; F1 can't).
az webapp config set -g rg-tenexcards-plc -n tenexcards-ka \
  --always-on true --min-tls-version 1.2 --ftps-state Disabled

# Logging BEFORE the deploy, so the very first startup is captured.
# Two calls, not one: --level is ignored unless --application-logging is present.
az webapp log config -g rg-tenexcards-plc -n tenexcards-ka \
  --docker-container-logging filesystem --level information
az webapp log config -g rg-tenexcards-plc -n tenexcards-ka \
  --application-logging filesystem --level information

# Build + package (not Azure, but the zip shape is the #1 way this goes wrong —
# a nested zip deploys "successfully" and then 503s).
dotnet publish TenExCards/TenExCards.csproj -c Release
# PowerShell; the trailing * is what flattens it:
#   Compress-Archive -Path bin/Release/net10.0/publish/* -DestinationPath bin/publish.zip -Force

# Ship it. --track-status polls until the container actually starts, so a
# green exit means "running", not merely "uploaded".
az webapp deploy -g rg-tenexcards-plc -n tenexcards-ka \
  --src-path TenExCards/bin/publish.zip --type zip --track-status true
The dead ends — mutating commands that ran and were then undone. Including these because they're the substance of your third question:


az group create --name rg-tenexcards-weu --location westeurope
az appservice plan create ... --location westeurope        # FAILED: RequestDisallowedByAzure
az group delete --name rg-tenexcards-weu --yes             # empty; I had just created it

az group create --name rg-tenexcards-neu --location northeurope
az appservice plan create ... --location northeurope       # FAILED: Current Limit (B1 VMs): 0

# Not in the plan — I invented this to tell a region-specific quota from a
# subscription-wide one. Both SUCCEEDED, i.e. created billable resources.
az appservice plan create --name asp-quota-probe-swedencentral ... --sku B1
az appservice plan create --name asp-quota-probe-polandcentral ... --sku B1
az appservice plan delete ... # x2, ~90 seconds later
az group delete --name rg-tenexcards-neu --yes             # empty by then
Also worth knowing: az auto-registered the Microsoft.Web resource provider on your subscription during the first plan create, without asking. That's normal CLI behaviour and it's permanent, but it is a subscription-level change you didn't type.

2. Bicep
Design decisions in the template worth flagging:

reserved: true is the actual Linux switch. kind: 'linux' is cosmetic — assert on reserved.
appSettings is deliberately not declared. Declaring appSettings: [] would make the template authoritative and silently wipe anything set via az webapp config appsettings set on every deployment — including the LLM API key and connection string that infrastructure.md plans to add as Key Vault references. Drift is the lesser evil. The template says so in a comment so the next person doesn't "fix" it.
Code deployment is not in the template. Zip push is a data-plane operation; it stays imperative.
az deployment group what-if is in the header comments — it's the terraform plan equivalent and the habit that makes declarative infra actually safer than the CLI.