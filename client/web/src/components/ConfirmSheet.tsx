import type { ReactNode } from "react";
import {
	Box,
	Button,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	Drawer,
	Stack,
	Typography,
	useMediaQuery,
	useTheme,
} from "@mui/material";
import { DISPLAY_FONT, tokens } from "../theme";

/**
 * Confirmation for a destructive action: a bottom sheet on phones (thumb reach, full-width
 * 48px buttons) and a small centred dialog from `sm` up.
 */
export default function ConfirmSheet({
	open,
	title,
	children,
	confirmLabel,
	cancelLabel = "Cancel",
	onConfirm,
	onClose,
	busy = false,
}: {
	open: boolean;
	title: string;
	children: ReactNode;
	confirmLabel: string;
	cancelLabel?: string;
	onConfirm: () => void;
	onClose: () => void;
	busy?: boolean;
}) {
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));
	const titleId = "confirm-sheet-title";
	const close = busy ? undefined : onClose;

	if (isXs) {
		return (
			<Drawer
				anchor="bottom"
				open={open}
				onClose={close}
				PaperProps={{
					role: "dialog",
					"aria-modal": true,
					"aria-labelledby": titleId,
					sx: {
						borderRadius: "20px 20px 0 0",
						px: 2,
						pt: 1.25,
						pb: "calc(20px + env(safe-area-inset-bottom))",
						boxShadow: "0 -12px 40px rgba(20,22,30,.18)",
					},
				} as any}
			>
				<Box sx={{ width: 36, height: 4, borderRadius: 4, bgcolor: tokens.lineStrong, alignSelf: "center", mb: 1.75 }} />
				<Typography
					id={titleId}
					component="h2"
					sx={{ fontFamily: DISPLAY_FONT, fontWeight: 800, fontSize: 19, letterSpacing: "-0.02em", mb: 1.5 }}
				>
					{title}
				</Typography>
				<Box sx={{ mb: 2 }}>{children}</Box>
				<Stack spacing={1}>
					<Button
						variant="contained"
						color="error"
						onClick={onConfirm}
						disabled={busy}
						sx={{ height: 48, fontSize: 15, fontWeight: 700 }}
					>
						{confirmLabel}
					</Button>
					<Button variant="outlined" onClick={onClose} disabled={busy} sx={{ height: 48, fontSize: 15 }}>
						{cancelLabel}
					</Button>
				</Stack>
			</Drawer>
		);
	}

	return (
		<Dialog open={open} onClose={close} aria-labelledby={titleId} fullWidth maxWidth="xs">
			<DialogTitle id={titleId} sx={{ fontSize: 20, pb: 1 }}>
				{title}
			</DialogTitle>
			<DialogContent>{children}</DialogContent>
			<DialogActions sx={{ px: 3, pb: 2.5, gap: 1 }}>
				<Button variant="outlined" onClick={onClose} disabled={busy} sx={{ height: 44 }}>
					{cancelLabel}
				</Button>
				<Button variant="contained" color="error" onClick={onConfirm} disabled={busy} sx={{ height: 44, fontWeight: 700 }}>
					{confirmLabel}
				</Button>
			</DialogActions>
		</Dialog>
	);
}
