# Supabase backend

Edge functions and migrations for QuoteCraft's hosted mode. Restored from the private `Workflow` snapshot (2026-09-14) after `Uno-Builds` was deleted; this repo had the client (`QuoteCraft/Data/Remote`) but not the backend.

- `migrations/`: tables (`001`) and row-level security (`002`).
- `functions/create-checkout-session`, `functions/stripe-webhook`: Stripe subscription checkout and webhook.
- `functions/quote-view`: public quote view.

Secrets are read from the function environment, never from source. Set these in Supabase:

- `STRIPE_BUSINESS_ANNUAL_PRICE_ID`
- `STRIPE_BUSINESS_MONTHLY_PRICE_ID`
- `STRIPE_PRO_ANNUAL_PRICE_ID`
- `STRIPE_PRO_MONTHLY_PRICE_ID`
- `STRIPE_SECRET_KEY`
- `STRIPE_WEBHOOK_SECRET`
- `SUPABASE_SERVICE_ROLE_KEY`
- `SUPABASE_URL`
