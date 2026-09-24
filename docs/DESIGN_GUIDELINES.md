# Frontend Interface Design Guidelines

This document outlines the official frontend interface design guidelines, aesthetic standards, and component specifications for all user interface developments. All UI elements, layout structures, and interactive experiences are standardized based on the official [MUI Material UI Component System](https://mui.com/material-ui/all-components/).

---

## 1. Design Philosophy & Core Principles

The design system prioritizes visual clarity, responsive adaptability, accessible ergonomics, and modern aesthetics.

* **Intentional Hierarchy:** Every screen must convey clear visual hierarchy using scale, weight, elevation, and spacing.
* **Component-First Consistency:** Every UI element must map directly to standardized MUI component patterns and specifications.
* **Predictable Interactions:** Consistent visual feedback for hover, active, focus, disabled, and loading states across all elements.
* **Responsive Fluidity:** Adaptive layouts accommodating mobile viewports through ultra-wide desktop monitors without layout breakage.
* **Accessible by Default:** All interfaces adhere to WCAG 2.1 AA standards for color contrast, keyboard navigation, and semantic labeling.

> **Official Component Specification Reference:**
> Explore the full catalog of components, specifications, and interactive demonstrations at the [MUI Material UI Components Webpage](https://mui.com/material-ui/all-components/).

---

## 2. Design Tokens & Foundations

### 2.1 Color Palette System

The color system uses semantic color roles designed for both light and dark themes.

| Role | Token Name | Light Value | Dark Value | Usage |
| :--- | :--- | :--- | :--- | :--- |
| **Primary** | `primary.main` | `#1976d2` | `#90caf9` | Main branding, primary action buttons, active states |
| **Primary (Light/Dark)** | `primary.light` / `primary.dark` | `#42a5f5` / `#1565c0` | `#e3f2fd` / `#42a5f5` | Hover states, focus rings, subtle backgrounds |
| **Secondary** | `secondary.main` | `#9c27b0` | `#ce93d8` | Accent actions, secondary highlights, tags |
| **Success** | `success.main` | `#2e7d32` | `#66bb6a` | Confirmation dialogs, positive status chips, alerts |
| **Warning** | `warning.main` | `#ed6c02` | `#ffa726` | Cautionary notices, pending states, non-destructive alerts |
| **Error** | `error.main` | `#d32f2f` | `#f44336` | Destructive actions, validation errors, critical alerts |
| **Info** | `info.main` | `#0288d1` | `#29b6f6` | Informational callouts, neutral badges, system tips |
| **Background (Default)**| `background.default` | `#f8fafc` | `#121212` | Main viewport canvas |
| **Background (Paper)** | `background.paper` | `#ffffff` | `#1e1e1e` | Cards, dialogs, drawers, surface menus |
| **Text (Primary)** | `text.primary` | `rgba(0,0,0,0.87)` | `rgba(255,255,255,0.87)`| Main headings, titles, high-emphasis text |
| **Text (Secondary)** | `text.secondary` | `rgba(0,0,0,0.60)` | `rgba(255,255,255,0.60)`| Body descriptions, captions, subtitle labels |
| **Text (Disabled)** | `text.disabled` | `rgba(0,0,0,0.38)` | `rgba(255,255,255,0.38)`| Inactive controls, placeholder text |
| **Divider** | `divider` | `rgba(0,0,0,0.12)` | `rgba(255,255,255,0.12)`| Section separators, list item borders |

> Detailed theming parameters can be reviewed on the [MUI Theming & Palette Guide](https://mui.com/material-ui/customization/palette/).

---

### 2.2 Typography Scale

Typography follows a baseline scale based on clean geometric sans-serif typefaces (e.g., *Inter*, *Roboto*, or system-ui fallback).

| Variant | Font Size | Weight | Line Height | Letter Spacing | Semantic Mapping |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **h1** | `2.5rem` (40px) | 700 / Bold | 1.2 | `-0.01562em` | Main page title / Hero |
| **h2** | `2.0rem` (32px) | 600 / SemiBold | 1.25 | `-0.00833em` | Primary section headers |
| **h3** | `1.75rem` (28px)| 600 / SemiBold | 1.3 | `0em` | Major module headers |
| **h4** | `1.5rem` (24px) | 600 / SemiBold | 1.35 | `0.00735em` | Card group titles, modal headers |
| **h5** | `1.25rem` (20px)| 500 / Medium | 1.4 | `0em` | Card headers, table titles |
| **h6** | `1.0rem` (16px) | 500 / Medium | 1.5 | `0.0075em` | Sub-card headers, toolbar titles |
| **subtitle1**| `1.0rem` (16px) | 400 / Regular | 1.75 | `0.00938em` | Primary subtitle text |
| **subtitle2**| `0.875rem` (14px)| 500 / Medium | 1.57 | `0.00714em` | Secondary subtitle, list section headers |
| **body1** | `1.0rem` (16px) | 400 / Regular | 1.5 | `0.00938em` | Primary body content, long paragraphs |
| **body2** | `0.875rem` (14px)| 400 / Regular | 1.43 | `0.01071em` | Compact body text, table cells, hints |
| **button** | `0.875rem` (14px)| 600 / SemiBold | 1.75 | `0.02857em` | Interactive control labels |
| **caption** | `0.75rem` (12px) | 400 / Regular | 1.66 | `0.03333em` | Timestamps, metadata, helper text |
| **overline**| `0.75rem` (12px) | 600 / SemiBold | 2.66 | `0.08333em` | Category tags, uppercase badges |

> Learn more at the [MUI Typography Webpage](https://mui.com/material-ui/react-typography/).

---

### 2.3 Spacing & 8pt Grid System

Spacing must adhere strictly to multiples of the **8px base unit** (with 4px half-steps used exclusively for compact elements and icons).

* `spacing(0.5)` = **4px** (Icon paddings, micro-tags, badge offsets)
* `spacing(1)` = **8px** (Compact input padding, button icon gap, tight chip gaps)
* `spacing(2)` = **16px** (Standard container gutters, card padding, form field gaps)
* `spacing(3)` = **24px** (Major section padding, modal body padding, grid column gap)
* `spacing(4)` = **32px** (Page container padding, hero margins, header spacing)
* `spacing(6)` = **48px** (Major layout separation, dashboard card row gaps)
* `spacing(8)` = **64px** (Landing section dividers, maximum viewport offsets)

---

### 2.4 Elevation, Shadows & Depth

Depth is conveyed through standardized elevation steps:

* **Elevation 0 (Flat):** Outlined surfaces, table rows, flat embedded panels. `border: 1px solid divider`.
* **Elevation 1 (Cards):** Standard content cards, data widgets. `box-shadow: 0px 2px 1px -1px rgba(0,0,0,0.2), 0px 1px 1px 0px rgba(0,0,0,0.14), 0px 1px 3px 0px rgba(0,0,0,0.12)`.
* **Elevation 2 (Floating Surfaces):** Hovered cards, quick-filter popovers, floating toolbars.
* **Elevation 4 (Menus / Dropdowns):** Context menus, autocomplete dropdowns, select menus.
* **Elevation 8 (Modals & Drawers):** Modal dialogs, side-navigation drawers, confirmation popups.
* **Elevation 16-24 (Snackbars / Tooltips):** Floating toasts, high-priority notifications, rich tooltips.

> In dark mode, elevation is supplemented by surface lighten overlays (higher elevation surfaces receive a brighter background tint).

---

### 2.5 Shape & Border Radius

* **Small Controls (`4px`):** Checkboxes, small tooltips, code chips.
* **Standard Elements (`8px`):** Buttons, text fields, select inputs, alerts, standard cards.
* **Large Containers (`12px` - `16px`):** Modal dialogs, major dashboard widgets, flyout drawers.
* **Pill / Circular (`9999px`):** User avatars, status chips, floating action buttons (FAB), toggle pills.

---

## 3. Layout & Grid Architecture

Interfaces are structured using standardized layout containers:

### 3.1 Breakpoint System

| Breakpoint | Window Width | Target Device / Viewport |
| :--- | :--- | :--- |
| **xs** | `0px - 599px` | Mobile phones (single-column layout, full-width sheets) |
| **sm** | `600px - 899px` | Tablets & large phones (2-column grids, collapsible drawer) |
| **md** | `900px - 1199px` | Small laptops / tablets landscape (3-column grids, side navigation) |
| **lg** | `1200px - 1535px`| Desktop displays (4-column cards, multi-pane views) |
| **xl** | `1536px+` | Ultra-wide monitors (max-width constrained dashboard grids) |

### 3.2 Layout Containers

* **Container:** Centers content horizontally with responsive max-width boundaries (`xs`, `sm`, `md`, `lg`, `xl`, or `fixed`).
* **Grid (12-Column Layout):** Fluid grid system supporting responsive column sizing (`xs={12} sm={6} md={4} lg={3}`).
* **Stack:** One-dimensional flexbox container managing directional layouts (row or column) with uniform spacing and alignment.
* **Box:** Standard wrapper for arbitrary styling, margin, and padding utilities.
* **App Bar & Toolbar:** Top navigation container providing branding, search bar, and user profile actions.
* **Drawer:** Navigation panels supporting `temporary` (mobile overlay), `persistent` (collapsible side menu), or `permanent` (fixed sidebar) modes.

> Reference specifications: [MUI Layout Grid Documentation](https://mui.com/material-ui/react-grid2/) and [MUI Container Documentation](https://mui.com/material-ui/react-container/).

---

## 4. Component Standards & Catalog

All frontend elements must adhere to the structural patterns documented in the [MUI Material UI Component Catalog](https://mui.com/material-ui/all-components/).

### 4.1 Buttons & Interactive Controls

#### Button
* **Contained Button:** Primary visual CTA on a screen (e.g., "Save & Apply", "Install Mod", "Create Item"). Limit to one primary contained button per logical section.
* **Outlined Button:** Secondary actions with equal priority but less visual noise (e.g., "Load Repository", "Cancel", "Configure").
* **Text Button:** Low-emphasis actions (e.g., "Learn More", "Dismiss", card footer actions).
* **Sizes:** `small` (28px height for table actions), `medium` (36px height default), `large` (42px height for landing CTAs).
* **Icon Buttons:** Use 40x40px touch targets with embedded tooltips for icon-only actions (e.g., delete, edit, refresh).

> Reference: [MUI Button Component](https://mui.com/material-ui/react-button/)

#### Button Group & Toggle Button Group
* Use **Button Group** for related actions with joined borders.
* Use **Toggle Button Group** for switching between mutually exclusive display modes (e.g., Grid View vs. List View, Theme Light vs. Dark).

> Reference: [MUI Toggle Button Component](https://mui.com/material-ui/react-toggle-button/)

---

### 4.2 Inputs & Form Controls

* **Text Field:**
  * Use the **Outlined** variant as the universal application standard.
  * Always include a clear `label` and optional `helperText`.
  * Display validation errors with error color styling and informative helper messages.
  * Support start/end adornments for currency, search icons, or clear buttons.
* **Select & Autocomplete:**
  * Standard dropdowns for short, static option lists (< 10 items).
  * Autocomplete / Searchable dropdowns for dynamic, large, or filterable datasets.
* **Checkbox & Switch:**
  * Use **Switch** for immediate, standalone setting activations (e.g., "Enable Server Mod", "Live Refresh").
  * Use **Checkbox** for multi-item selection in lists or forms requiring a submission trigger.
* **Slider:**
  * Use for continuous or discrete range values (e.g., opacity, volume, font scaling) with value labels and step marks.

> Reference: [MUI Text Field](https://mui.com/material-ui/react-text-field/) and [MUI Autocomplete](https://mui.com/material-ui/react-autocomplete/)

---

### 4.3 Data Display

#### Card
Cards serve as the primary container for discrete information units (e.g., Mod Cards, Theme Tiles, Setting Groups).
* **CardHeader:** Contains title, optional subheader, and optional action (e.g., menu button, status badge).
* **CardMedia:** Aspect-ratio constrained image or preview banner. All card media containers must enforce a standardized minimum height (`min-height: 160px; height: 160px;`) with `object-fit: cover` and `width: 100%` to ensure preview artwork, banners, and fallback placeholders are always fully visible and never collapsed or truncated.
* **CardContent:** Body typography, descriptions, and metadata chips.
* **CardActions:** Standardized bottom action bar aligned left or right with standard button gaps.

> Reference: [MUI Card Component](https://mui.com/material-ui/react-card/)

#### Chip
* Use for status indicators, categories, keyword tags, or active filter badges.
* Support `filled` and `outlined` variants.
* Use semantic colors: `success` (Active / Installed), `warning` (Update Available), `error` (Error / Disabled), `default` (Category tag).

> Reference: [MUI Chip Component](https://mui.com/material-ui/react-chip/)

#### Table & Data Grid
* Striped or clean bordered table layouts with sticky headers for scrolling views.
* Column headers must support sorting indicators, text alignment (left for text, right for numeric data), and fixed action columns.
* Integrated pagination controls at the table footer.

> Reference: [MUI Table Component](https://mui.com/material-ui/react-table/)

#### Badge & Avatar
* Use **Avatar** for user profiles, theme author icons, or repository sources (supporting circular and rounded variants).
* Use **Badge** for unread counters, notification dots, or online status indicators anchored to avatars or icons.

> Reference: [MUI Avatar Component](https://mui.com/material-ui/react-avatar/) and [MUI Badge Component](https://mui.com/material-ui/react-badge/)

---

### 4.4 Feedback & Status

* **Alert:**
  * Four semantic types: `error`, `warning`, `info`, `success`.
  * Variants: `standard` (soft tint background), `filled` (high-contrast solid background), `outlined` (clean bordered style).
  * Always provide actionable resolution links or dismiss triggers where applicable.
* **Snackbar (Toasts):**
  * Display brief, floating status updates anchored to `bottom-right` or `bottom-center`.
  * Default auto-dismiss timeout: `4000ms` - `6000ms`.
* **Dialog (Modal):**
  * Used for critical confirmations, destructive warnings, or complex modal workflows.
  * Structure: `DialogTitle`, `DialogContent`, and `DialogActions`.
  * Must support keyboard dismiss via `Escape` and backdrop click handling (unless an in-progress operation requires completion).
* **Progress & Skeleton:**
  * Use **Skeleton** screens matching the exact layout of loading cards or tables to prevent layout shift.
  * Use **CircularProgress** for button loading states and spinner spinners.
  * Use **LinearProgress** for file downloads, upload transfers, or progress bars.

> Reference: [MUI Alert Component](https://mui.com/material-ui/react-alert/) and [MUI Dialog Component](https://mui.com/material-ui/react-dialog/)

---

### 4.5 Navigation & Surfaces

#### 4.5.1 Tabs Component Specification
Tabs organize content across different screens, data sets, and other interactions into mutually exclusive tab panels. All tab implementations must adhere to the official [MUI Tabs Component Specification](https://mui.com/material-ui/react-tabs/).

* **Anatomy & Structure:**
  * **Tabs Root (`.mui-tabs` / `role="tablist"`):** Flex container running horizontally with a bottom border (`1px solid var(--mui-palette-divider)`).
  * **Tab Item (`.mui-tab` / `role="tab"`):** Interactive child representing each panel view.
  * **Active Indicator:** Bottom indicator line (`2px solid var(--mui-palette-primary-main)`) anchored seamlessly to the container's bottom edge (`margin-bottom: -1px`).
  * **Tab Icons & Badges:** Optional start icon (`iconPosition="start"`) or end badge displaying counters.
* **Dimensions & Typography:**
  * **Height:** Minimum height of `48px` (`padding: 12px 20px`).
  * **Typography:** `0.875rem` (14px), uppercase, letter spacing `0.02857em`, `font-weight: 500` (default) / `600` (active).
* **Interactive States:**
  * **Default:** `color: var(--mui-palette-text-secondary)` (`rgba(255, 255, 255, 0.60)`).
  * **Hover:** `color: var(--mui-palette-text-primary)` with subtle surface highlight `rgba(255, 255, 255, 0.04)` and `4px` top border-radius.
  * **Active / Selected:** `color: var(--mui-palette-primary-main)` (`#90caf9`), `border-bottom: 2px solid var(--mui-palette-primary-main)`, `font-weight: 600`.
  * **Focus-Visible:** Accessible outline ring (`outline: 2px solid var(--mui-palette-primary-main); outline-offset: -2px;`).
  * **Disabled:** `opacity: 0.38; pointer-events: none;`.

* **Breadcrumbs:**
  * For deep hierarchical navigation (e.g., `Dashboard > Themes > Marketplace > Theme Details`).
* **Accordion:**
  * For collapsible setting groups or FAQ lists with expand/collapse arrow indicators.
* **Menu & Popover:**
  * Standard dropdown menus for item actions (e.g., "More Options", "Export", "Delete").

> Reference: [MUI Tabs Component](https://mui.com/material-ui/react-tabs/) and [MUI Accordion Component](https://mui.com/material-ui/react-accordion/)

---

## 5. Accessibility (a11y) & UX Guidelines

1. **Color Contrast:** Text and interactive elements must satisfy minimum 4.5:1 contrast against their background (3:1 for large headers and graphical boundaries).
2. **Keyboard Navigation:** All interactive elements must be accessible via `Tab`, `Shift+Tab`, `Enter`, `Space`, and arrow keys.
3. **Focus Indicators:** Never remove focus outlines without supplying an accessible high-visibility focus ring (`2px solid primary.main` with 2px offset).
4. **Touch Target Sizing:** Interactive buttons and click areas must have a minimum physical bounding box of **44x44px** on touch devices.
5. **No Icon Without Context:** Every icon-only button must have an accessible `aria-label` and an accompanying visual `Tooltip`.
6. **Destructive Action Safety:** Any destructive operation (e.g., deletion, cache reset, purge) must require secondary confirmation via a Dialog.

---

## 6. Iconography Standards

* **MUI Material Icons (SVG) Exclusively:**
  * All iconography across the interface must be rendered as vector SVGs conforming to the official [MUI Material Icons Library](https://mui.com/material-ui/material-icons/).
  * Use the standard `24x24px` viewport grid (`viewBox="0 0 24 24"`) with `fill="currentColor"` or standard strokes to automatically inherit parent font colors and token states.
  * Use consistent icon variants across the application (preferring **Outlined** or **Rounded** variants for clean, modern interfaces).
* **Strict Prohibition of Emojis:**
  * Raw Unicode emojis are **strictly forbidden** across all user interfaces, components, and documentation.
  * Emojis render inconsistently across operating systems and browsers, fail to inherit theme color tokens (`currentColor`), and break visual harmony. All iconography must be rendered using vector SVGs from the official [MUI Material Icons Library](https://mui.com/material-ui/material-icons/).
* **Standard Icon Sizing:**
  * `small`: `18x18px` (inline text hints, compact chips, badge icons)
  * `medium`: `24x24px` (standard button icons, tab start icons, menu items, table rows)
  * `large`: `36x36px` - `48x48px` (empty state banners, hero module illustrations)
* **Icon Target & Alignment:**
  * Inline icon adornments must align vertically with neighboring text (`display: inline-flex; align-items: center; justify-content: center;`).
  * Standalone icon buttons must have minimum `40x40px` touch bounding boxes and provide accessible `aria-label` tags.

---

## 7. Official Documentation & Learning Resources

For detailed component behaviors, visual examples, design tokens, and interactive sandboxes, refer directly to the official MUI webpages:

* **Component Overview & Catalog:** [https://mui.com/material-ui/all-components/](https://mui.com/material-ui/all-components/)
* **Theming & Color System:** [https://mui.com/material-ui/customization/theming/](https://mui.com/material-ui/customization/theming/)
* **Typography Specifications:** [https://mui.com/material-ui/react-typography/](https://mui.com/material-ui/react-typography/)
* **Material Icons Directory:** [https://mui.com/material-ui/material-icons/](https://mui.com/material-ui/material-icons/)
* **Layout & Spacing Architecture:** [https://mui.com/material-ui/react-grid2/](https://mui.com/material-ui/react-grid2/)
* **Dark Mode Principles:** [https://mui.com/material-ui/customization/dark-mode/](https://mui.com/material-ui/customization/dark-mode/)
