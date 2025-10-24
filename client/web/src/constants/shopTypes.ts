export const ShopTypes = {
  COLES: 0,
  WOOLWORTHS: 1,
} as const;

export type ShopTypeValue = typeof ShopTypes[keyof typeof ShopTypes];

export const shopTypeName = (value: unknown): string => {
  if (value === ShopTypes.COLES || value === `${ShopTypes.COLES}`) return "Coles";
  if (value === ShopTypes.WOOLWORTHS || value === `${ShopTypes.WOOLWORTHS}`) return "Woolworths";
  return value == null || value === "" ? "-" : String(value);
};

export const SHOP_OPTIONS: Array<{ label: string; value: '' | string }> = [
  { label: "All", value: "" },
  { label: "Coles", value: `${ShopTypes.COLES}` },
  { label: "Woolworths", value: `${ShopTypes.WOOLWORTHS}` },
];
