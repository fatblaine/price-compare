import { createTheme } from "@mui/material/styles";

/**
 * Clean retail theme (BTS-161), from the "PricePeer 设计精进" design canvas:
 * one indigo brand, two store colours, green for savings, amber for promos;
 * white cards with hairline borders on a light grey ground. Plus Jakarta Sans
 * for display text and prices, Inter for UI. No glass, gradients or blur.
 */
export const tokens = {
	ink: "#14161B",
	ink2: "#565E6E",
	ink3: "#767E8D",
	ground: "#F4F5F7",
	surface: "#FFFFFF",
	line: "#E6E8EE",
	lineStrong: "#C9CCD6",
	imageGround: "#FAFBFC",
	brand: "#4F46E5",
	brandDark: "#3B32C9",
	brandTint: "#EEEFFE",
	save: "#0B7A43",
	saveTint: "#E3F4EA",
	promo: "#8A5A00",
	promoTint: "#F7EFDF",
	fav: "#E0407A",
	cardShadow: "0 1px 2px rgba(20,22,30,.04), 0 12px 28px rgba(20,22,30,.05)",
} as const;

export const DISPLAY_FONT = [
	'"Plus Jakarta Sans"',
	'"Inter"',
	"system-ui",
	"-apple-system",
	"sans-serif",
].join(",");

const UI_FONT = [
	'"Inter"',
	"system-ui",
	"-apple-system",
	'"Segoe UI"',
	"Roboto",
	"sans-serif",
].join(",");

const theme = createTheme({
	palette: {
		mode: "light",
		primary: { main: tokens.brand, dark: tokens.brandDark, contrastText: "#ffffff" },
		secondary: { main: tokens.brandDark, contrastText: "#ffffff" },
		success: { main: tokens.save },
		warning: { main: tokens.promo },
		error: { main: "#C62828" },
		background: { default: tokens.ground, paper: tokens.surface },
		text: { primary: tokens.ink, secondary: tokens.ink2 },
		divider: tokens.line,
	},
	shape: { borderRadius: 12 },
	typography: {
		fontFamily: UI_FONT,
		h1: { fontFamily: DISPLAY_FONT, fontWeight: 800, letterSpacing: "-0.03em" },
		h2: { fontFamily: DISPLAY_FONT, fontWeight: 800, letterSpacing: "-0.03em" },
		h3: { fontFamily: DISPLAY_FONT, fontWeight: 800, letterSpacing: "-0.02em" },
		h4: { fontFamily: DISPLAY_FONT, fontWeight: 800, letterSpacing: "-0.02em" },
		h5: { fontFamily: DISPLAY_FONT, fontWeight: 700, letterSpacing: "-0.02em" },
		h6: { fontFamily: DISPLAY_FONT, fontWeight: 700, letterSpacing: "-0.01em" },
		button: { textTransform: "none", fontWeight: 600 },
	},
	components: {
		MuiCssBaseline: {
			styleOverrides: {
				body: { backgroundColor: tokens.ground },
			},
		},
		MuiButton: {
			defaultProps: { disableElevation: true },
			styleOverrides: {
				root: { borderRadius: 12, paddingInline: 18, fontWeight: 600 },
				containedPrimary: {
					backgroundColor: tokens.brand,
					"&:hover": { backgroundColor: tokens.brandDark },
				},
				outlined: {
					backgroundColor: tokens.surface,
					borderColor: tokens.line,
					color: tokens.ink,
					"&:hover": { backgroundColor: tokens.surface, borderColor: tokens.lineStrong },
				},
			},
		},
		MuiCard: {
			defaultProps: { elevation: 0 },
			styleOverrides: {
				root: {
					borderRadius: 16,
					backgroundColor: tokens.surface,
					border: `1px solid ${tokens.line}`,
					boxShadow: tokens.cardShadow,
					backgroundImage: "none",
				},
			},
		},
		MuiPaper: {
			styleOverrides: {
				root: { backgroundImage: "none" },
			},
		},
		MuiDialog: {
			styleOverrides: {
				paper: {
					borderRadius: 18,
					backgroundColor: tokens.surface,
					boxShadow: "0 24px 70px rgba(20,22,30,0.18)",
				},
				paperFullScreen: {
					borderRadius: 0,
					backgroundColor: tokens.surface,
				},
			},
		},
		MuiDialogTitle: {
			styleOverrides: {
				root: { fontFamily: DISPLAY_FONT, fontWeight: 800, letterSpacing: "-0.02em" },
			},
		},
		MuiChip: {
			styleOverrides: {
				root: { borderRadius: 999, fontWeight: 600 },
			},
		},
		MuiOutlinedInput: {
			styleOverrides: {
				root: {
					borderRadius: 12,
					backgroundColor: tokens.surface,
					"& fieldset": { borderColor: tokens.line },
					"&:hover fieldset": { borderColor: tokens.lineStrong },
				},
			},
		},
		MuiTab: {
			styleOverrides: {
				root: { textTransform: "none", fontWeight: 600 },
			},
		},
		MuiPaginationItem: {
			styleOverrides: {
				root: { fontWeight: 600 },
			},
		},
	},
});

export default theme;
