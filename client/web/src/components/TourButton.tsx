import * as React from "react";
import Button from "@mui/material/Button";
import Tooltip from "@mui/material/Tooltip";
import HelpOutlineIcon from "@mui/icons-material/HelpOutline";
import { tokens } from "../theme";

interface TourButtonProps {
	onClick: () => void;
	disabled?: boolean;
}

/**
 * Starts the guided tour. A compact "?" in the search bar, styled like the AI
 * button beside it, instead of a labelled pill in the filter row (BTS-161).
 */
export default function TourButton({ onClick, disabled }: TourButtonProps) {
	return (
		<Tooltip title="Take a quick tour">
			<span>
				<Button
					variant="outlined"
					onClick={onClick}
					disabled={disabled}
					aria-label="Tour / Help"
					sx={{
						height: { xs: 46, sm: 44 },
						width: { xs: 46, sm: 44 },
						minWidth: 0,
						px: 0,
						color: tokens.ink2,
					}}
				>
					<HelpOutlineIcon aria-hidden="true" sx={{ fontSize: 20 }} />
				</Button>
			</span>
		</Tooltip>
	);
}
