import * as React from "react";
import Box from "@mui/material/Box";
import { shopColors, shopTypeName } from "../constants/shopTypes";

/** Tinted store chip with a colour dot (BTS-161): never white text on the store colour. */
export default function StoreChip({
	shopType,
	size = "medium",
}: {
	shopType?: number | string | null;
	size?: "small" | "medium";
}) {
	const c = shopColors(shopType);
	const small = size === "small";
	return (
		<Box
			component="span"
			sx={{
				display: "inline-flex",
				alignItems: "center",
				gap: 0.75,
				height: small ? { xs: 22, sm: 24 } : 28,
				px: small ? { xs: 1, sm: 1.125 } : 1.25,
				borderRadius: 999,
				bgcolor: c.tint,
				color: c.text,
				fontSize: small ? { xs: 10.5, sm: 11.5 } : 12.5,
				fontWeight: 600,
				lineHeight: 1,
				whiteSpace: "nowrap",
				flexShrink: 0,
			}}
		>
			<Box
				component="span"
				aria-hidden="true"
				sx={{ width: 7, height: 7, borderRadius: "50%", bgcolor: c.main, flexShrink: 0 }}
			/>
			{shopTypeName(shopType)}
		</Box>
	);
}
