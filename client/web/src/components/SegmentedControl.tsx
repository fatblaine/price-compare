import type { ReactNode } from "react";
import ButtonBase from "@mui/material/ButtonBase";
import Box from "@mui/material/Box";
import type { SxProps, Theme } from "@mui/material/styles";
import { tokens } from "../theme";

export interface SegmentedOption<T extends string | number> {
	value: T;
	label: ReactNode;
}

/**
 * Grey track with a white "thumb" on the selected option (Admin tabs, time range, match type).
 * Each option is a real button with aria-pressed, so keyboard and screen readers work.
 */
export default function SegmentedControl<T extends string | number>({
	value,
	options,
	onChange,
	ariaLabel,
	height = 40,
	fullWidth = false,
	disabled = false,
	sx,
}: {
	value: T;
	options: SegmentedOption<T>[];
	onChange: (value: T) => void;
	ariaLabel: string;
	height?: number;
	fullWidth?: boolean;
	disabled?: boolean;
	sx?: SxProps<Theme>;
}) {
	return (
		<Box
			role="group"
			aria-label={ariaLabel}
			sx={[
				{
					display: fullWidth ? "grid" : "inline-grid",
					gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))`,
					gap: "2px",
					p: "3px",
					bgcolor: "#E9EBF0",
					borderRadius: "12px",
					width: fullWidth ? "100%" : "auto",
					boxSizing: "border-box",
				},
				...(Array.isArray(sx) ? sx : [sx]),
			]}
		>
			{options.map((opt) => {
				const selected = opt.value === value;
				return (
					<ButtonBase
						key={String(opt.value)}
						aria-pressed={selected}
						disabled={disabled}
						onClick={() => onChange(opt.value)}
						sx={{
							height,
							px: 1.75,
							borderRadius: "9px",
							fontSize: 13.5,
							fontWeight: selected ? 700 : 600,
							whiteSpace: "nowrap",
							color: selected ? tokens.ink : tokens.ink2,
							bgcolor: selected ? tokens.surface : "transparent",
							boxShadow: selected ? "0 1px 2px rgba(20,22,30,.1)" : "none",
							transition: "background-color .15s, color .15s",
							"&.Mui-focusVisible": { outline: `2px solid ${tokens.brand}`, outlineOffset: 1 },
						}}
					>
						{opt.label}
					</ButtonBase>
				);
			})}
		</Box>
	);
}
