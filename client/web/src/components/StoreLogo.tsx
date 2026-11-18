import Box from "@mui/material/Box";
import type { SxProps, Theme } from "@mui/material/styles";
// Bundled through the build, so each logo is one content-hashed file under
// static/media/ that the browser downloads once and every card reuses.
import colesLogo from "../assets/logos/coles.svg";
import woolworthsLogo from "../assets/logos/woolworths.svg";
import { ShopTypes, shopTypeName } from "../constants/shopTypes";

const LOGOS: Record<number, string> = {
	[ShopTypes.COLES]: colesLogo,
	[ShopTypes.WOOLWORTHS]: woolworthsLogo,
};

/**
 * Square store monogram that stands in for the store name where space is tight.
 * The full store name is the alt text, so screen readers still hear it; pass
 * `decorative` when the name is already written next to the logo.
 */
export default function StoreLogo({
	shopType,
	size = 18,
	decorative = false,
	sx,
}: {
	shopType?: number | string | null;
	size?: number;
	decorative?: boolean;
	sx?: SxProps<Theme>;
}) {
	const src = LOGOS[Number(shopType)];
	if (shopType == null || shopType === "" || !src) return null;
	return (
		<Box
			component="img"
			src={src}
			width={size}
			height={size}
			alt={decorative ? "" : shopTypeName(Number(shopType))}
			aria-hidden={decorative || undefined}
			draggable={false}
			sx={[
				{ display: "block", flexShrink: 0, width: size, height: size },
				...(Array.isArray(sx) ? sx : [sx]),
			]}
		/>
	);
}
