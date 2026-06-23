import { createTheme } from "@mui/material/styles";

/**
 * Glassmorphism ("liquid glass") theme inspired by docs/frontend-style.
 * Purple accent (#6e7bff → #8b6eff), translucent frosted surfaces,
 * rounded corners and soft ambient shadows. The animated colour-mesh
 * background lives in index.css (body::before); this theme styles the
 * glass surfaces that sit on top of it.
 */

const ACCENT = "#6e7bff";
const ACCENT_DARK = "#8b6eff";
const TEXT_PRIMARY = "#1d1d1f";
const TEXT_SECONDARY = "#86868b";

const SF_STACK = [
	"-apple-system",
	"BlinkMacSystemFont",
	'"SF Pro Display"',
	'"SF Pro Text"',
	'"Segoe UI"',
	'"Helvetica Neue"',
	"Roboto",
	"sans-serif",
].join(",");

const theme = createTheme({
	palette: {
		mode: "light",
		primary: { main: ACCENT, dark: ACCENT_DARK, contrastText: "#ffffff" },
		secondary: { main: ACCENT_DARK, contrastText: "#ffffff" },
		success: { main: "#1f9d57" },
		warning: { main: "#e08a1e" },
		error: { main: "#ff3b6b" },
		background: { default: "#eef0f4", paper: "rgba(255,255,255,0.62)" },
		text: { primary: TEXT_PRIMARY, secondary: TEXT_SECONDARY },
		divider: "rgba(120,120,128,0.2)",
	},
	shape: { borderRadius: 14 },
	typography: {
		fontFamily: SF_STACK,
		h4: { fontWeight: 700, letterSpacing: "-0.03em" },
		h5: { fontWeight: 700, letterSpacing: "-0.02em" },
		h6: { fontWeight: 700, letterSpacing: "-0.02em" },
		subtitle1: { letterSpacing: "-0.01em" },
		button: { textTransform: "none", fontWeight: 600, letterSpacing: "-0.01em" },
	},
	components: {
		MuiButton: {
			defaultProps: { disableElevation: true },
			styleOverrides: {
				root: { borderRadius: 13, paddingInline: 18 },
				containedPrimary: {
					background: `linear-gradient(135deg, ${ACCENT}, ${ACCENT_DARK})`,
					boxShadow: "0 6px 16px rgba(110,123,255,0.35)",
					"&:hover": {
						background: `linear-gradient(135deg, ${ACCENT}, ${ACCENT_DARK})`,
						filter: "brightness(1.06)",
						boxShadow: "0 8px 20px rgba(110,123,255,0.42)",
					},
				},
				outlinedPrimary: {
					borderColor: "rgba(110,123,255,0.4)",
					backgroundColor: "rgba(110,123,255,0.06)",
					"&:hover": {
						borderColor: ACCENT,
						backgroundColor: "rgba(110,123,255,0.14)",
					},
				},
			},
		},
		MuiCard: {
			styleOverrides: {
				root: {
					borderRadius: 22,
					backgroundColor: "rgba(255,255,255,0.6)",
					backdropFilter: "blur(24px) saturate(180%)",
					WebkitBackdropFilter: "blur(24px) saturate(180%)",
					border: "0.5px solid rgba(255,255,255,0.85)",
					boxShadow: "0 10px 34px rgba(50,55,95,0.1)",
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
					borderRadius: 24,
					backgroundColor: "rgba(255,255,255,0.78)",
					backdropFilter: "blur(30px) saturate(180%)",
					WebkitBackdropFilter: "blur(30px) saturate(180%)",
					border: "0.5px solid rgba(255,255,255,0.7)",
					boxShadow: "0 24px 70px rgba(50,55,95,0.22)",
				},
			},
		},
		MuiChip: {
			styleOverrides: {
				root: { borderRadius: 980, fontWeight: 600 },
			},
		},
		MuiOutlinedInput: {
			styleOverrides: {
				root: {
					borderRadius: 13,
					backgroundColor: "rgba(120,120,128,0.08)",
					"& fieldset": { borderColor: "rgba(120,120,128,0.18)" },
					"&:hover fieldset": { borderColor: "rgba(110,123,255,0.4)" },
				},
			},
		},
		MuiTab: {
			styleOverrides: {
				root: { textTransform: "none", fontWeight: 500, letterSpacing: "-0.01em" },
			},
		},
	},
});

export default theme;
