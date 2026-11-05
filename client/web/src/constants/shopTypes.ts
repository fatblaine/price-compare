export const ShopTypes = {
	COLES: 0,
	WOOLWORTHS: 1,
} as const;

export type ShopTypeValue = (typeof ShopTypes)[keyof typeof ShopTypes];

export const shopTypeName = (value: unknown): string => {
	if (value === ShopTypes.COLES || value === `${ShopTypes.COLES}`)
		return "Coles";
	if (value === ShopTypes.WOOLWORTHS || value === `${ShopTypes.WOOLWORTHS}`)
		return "Woolworths";
	return value == null || value === "" ? "-" : String(value);
};

export const SHOP_OPTIONS: Array<{ label: string; value: "" | string }> = [
	{ label: "All", value: "" },
	{ label: "Coles", value: `${ShopTypes.COLES}` },
	{ label: "Woolworths", value: `${ShopTypes.WOOLWORTHS}` },
];

/** Store colours (BTS-161 design): solid colour for dots/lines, tint + text for chips. */
export const shopColors = (
	value: unknown,
): { main: string; tint: string; text: string } => {
	if (value === ShopTypes.COLES || value === `${ShopTypes.COLES}`)
		return { main: "#C93925", tint: "#FBEAE7", text: "#A12E22" };
	if (value === ShopTypes.WOOLWORTHS || value === `${ShopTypes.WOOLWORTHS}`)
		return { main: "#3F8149", tint: "#E8F2EB", text: "#256B34" };
	return { main: "#565E6E", tint: "#F1F2F5", text: "#565E6E" };
};
