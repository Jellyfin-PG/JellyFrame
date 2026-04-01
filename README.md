![Build Status](https://img.shields.io/github/actions/workflow/status/Jellyfin-PG/Jellyframe/release.yml)
![License](https://img.shields.io/github/license/Jellyfin-PG/Jellyframe)

<div align="center">
  <img src="assets/jellyframe.png" alt="Jellyframe Logo" width="120" />
  <h1>Jellyframe</h1>
  <p>A complete customization and extension framework for [Jellyfin](https://jellyfin.org). Install community mods and themes from the dashboard, or build your own with a full server-side JavaScript API.</p>
</div>

---

## Installation

JellyFrame depends on the **File Transformation** plugin to inject CSS and JavaScript into Jellyfin's web interface. Both plugins must be installed from the same plugin catalogue — install them in the order below.

### Step 1 — Add the plugin catalogue

1. Open your Jellyfin dashboard
2. Go to **Administration → Plugins → Repositories**
3. Click **Add** and enter the following URL:

```
https://raw.githubusercontent.com/Jellyfin-PG/JellyFrame/main/manifest.json
```

4. Click **Save**

### Step 2 — Install File Transformation

1. Go to **Administration → Plugins → Catalogue**
2. Find **File Transformation** and click **Install**
3. When prompted, confirm the installation

### Step 3 — Install JellyFrame

1. Still in the **Catalogue**, find **JellyFrame** and click **Install**
2. When prompted, confirm the installation

### Step 4 — Restart Jellyfin

Restart your Jellyfin server. Both plugins must be active at the same time — File Transformation handles the page injection, JellyFrame manages your mods and themes.

### Step 5 — Confirm installation

After restarting, open your Jellyfin dashboard. You should see two new entries in the left-hand menu:

- **Mods**
- **Themes**

---

## Getting started

### Installing a mod

1. Go to **Dashboard → Mods → Marketplace**
2. Enter a `mods.json` repository URL and click **Load Mods**
3. Enable the mods you want — mods with configurable options will show a settings dialog
4. Click **Save & Apply**
5. Hard-refresh your Jellyfin tab with **Ctrl+Shift+R**

### Installing a theme

1. Go to **Dashboard → Themes → Marketplace**
2. Enter a `themes.json` repository URL and click **Load Themes**
3. Click **Apply** on a theme — themes with variables or optional addons will show a configuration dialog
4. Click **Save & Apply**
5. Hard-refresh your Jellyfin tab with **Ctrl+Shift+R**

Only one theme can be active at a time. Themes and mods stack — your active theme loads first, then mods inject on top.

---

## Troubleshooting

**The Mods or Themes menu entries are not showing**
Make sure both File Transformation and JellyFrame are installed and that Jellyfin has been fully restarted after installing both.

**A mod or theme update is not appearing after saving**
Go to the Settings tab of the relevant page and use the cache purge to force a fresh download, then hard-refresh with Ctrl+Shift+R.

**A server mod is not loading**
Check the Jellyfin log for entries tagged `[JellyFrame]`. Missing permissions or failed dependencies will be logged with a clear explanation.

---

## For creators

Looking to build your own mod or theme? See the [creator documentation](https://github.com/Jellyfin-PG/JellyFrame/wiki).

---

## Requirements

| Requirement | Version |
|---|---|
| Jellyfin | 10.10 or newer |
| File Transformation plugin | Latest |
| .NET runtime | 9.0 (bundled with Jellyfin) |
