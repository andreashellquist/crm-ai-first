// .NET's OpenAPI generator types nullable integers as `number | string` (a
// safe-interop convention for values that could exceed JS's safe integer
// range) — coerce once here rather than at every call site.
type NullableAmount = number | string | null | undefined;

export function formatAmount(cents: NullableAmount, currency: string | null | undefined) {
  if (cents == null) return null;
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: currency ?? "USD",
    maximumFractionDigits: 0,
  }).format(Number(cents) / 100);
}

export function sumCents(values: NullableAmount[]): number {
  return values.reduce<number>((sum, v) => sum + (v == null ? 0 : Number(v)), 0);
}
