# Cart Launch Companion 2.0 Design Principles

These principles are the project's decision-making framework. Features, layouts, configuration formats, dependencies, and implementation choices should be evaluated against them.

## 1. Browse → Confirm → Launch

The entire application supports one clear flow:

```text
Home
  ↓ Select a game and press A
Metadata
  ↓ Confirm and press A
Launch
```

The Home page never launches a game directly.

## 2. Controller First

The complete primary workflow must work with a controller.

- Directional navigation must be predictable.
- **A** confirms or advances.
- **B** returns or cancels.
- Button prompts must remain consistent.
- Mouse and keyboard support must follow the same actions.
- Controller navigation must work at every supported resolution.

## 3. Curated Library

The launcher is designed for a small, intentional collection, normally ten games or fewer.

The application does not need:

- Search
- Filters
- Collections
- Categories
- Infinite scrolling
- Large-library management tools

## 4. Portable

The application should run without a traditional installer.

- Windows and Linux builds may share one portable parent folder.
- User-managed data must remain outside runtime-specific build folders.
- Relative paths should be preferred.
- Moving the complete folder or drive should preserve the setup where platform launch paths permit it.

## 5. Cross-platform

Windows and Linux/SteamOS are first-class targets.

- One shared Avalonia interface.
- Shared configuration and metadata models.
- Platform-specific launching behind interfaces.
- Unsupported platform features must fail clearly and safely.
- Actual SteamOS hardware testing is required before release.

## 6. Launcher Agnostic

Steam, Xbox, Epic, Heroic, GOG, Ubisoft, Rockstar, Amazon, local games, and other launch methods should share one presentation language.

Launcher identity may control:

- Accent color
- Launcher glyph
- Lighting color
- Launch adapter

Launcher identity must not change the navigation model or page structure.

## 7. Performance

The launcher should feel immediate.

- Fast startup
- Low storage overhead
- Low idle resource use
- Smooth controller response
- Animations that do not block input
- Lazy loading where it improves responsiveness
- No unnecessary full-library rescans

## 8. Consistency

Home, Metadata, Exit, launch transitions, settings, and error states must share:

- Typography
- Button prompts
- Spacing
- Accent handling
- Lighting
- Animation timing
- Focus behavior
- Controller actions

A dialog must feel like part of the same application, not an operating-system popup.

## 9. User Ease

The application, folder structure, and configuration should be intuitive, organized, portable, and require minimal storage while remaining easy to understand and maintain.

### User-interface ease

- Clear screens with one purpose each
- No hidden primary actions
- No unnecessary settings
- Plain-language errors
- Consistent controller prompts
- Full mouse support for visible actions

### File-structure ease

A user should be able to locate a game's configuration, artwork, trailer, logs, and build files without documentation.

### Storage ease

- Avoid duplicate artwork.
- Keep caches disposable.
- Rotate or limit logs.
- Do not retain failed downloads.
- Do not add large dependencies without a clear benefit.
- Prefer compressed assets at appropriate resolutions.

## Decision test

Before adding a feature, ask:

1. Does it support Browse, Confirm, or Launch?
2. Can it be used comfortably with a controller?
3. Does it keep the portable folder understandable?
4. Does it work consistently across supported platforms?
5. Does its value justify its code, storage, and maintenance cost?

If the answer is no, redesign it, move it out of the primary experience, or leave it out.
