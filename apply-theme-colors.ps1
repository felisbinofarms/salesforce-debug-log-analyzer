# Apply Theme Colors Script
# Replaces all hardcoded hex colors with theme resource bindings

Write-Host "Applying Theme Colors to XAML Files..." -ForegroundColor Cyan

# Get all XAML files in Views folder
$xamlFiles = Get-ChildItem -Path "Views\*.xaml" -Recurse

# Color mapping: Hardcoded hex -> Theme resource
$colorMappings = @{
    # Backgrounds
    'Background="#383A40"' = 'Background="{StaticResource BgInput}"'
    'Background="#404249"' = 'Background="{StaticResource BgHover}"'
    'Background="#1E1F22"' = 'Background="{StaticResource BgTertiary}"'
    'Background="#2B2D31"' = 'Background="{StaticResource BgSecondary}"'
    
    # Foregrounds (Text colors)
    'Foreground="#B5BAC1"' = 'Foreground="{StaticResource TextSecondary}"'
    'Foreground="#72767D"' = 'Foreground="{StaticResource TextMuted}"'
    'Foreground="#DBDEE1"' = 'Foreground="{StaticResource TextPrimary}"'
    
    # Accent colors
    'Background="#5865F2"' = 'Background="{StaticResource AccentPrimary}"'
    'BorderBrush="#5865F2"' = 'BorderBrush="{StaticResource AccentPrimary}"'
    'Stroke="#5865F2"' = 'Stroke="{StaticResource AccentPrimary}"'
    'Foreground="#5865F2"' = 'Foreground="{StaticResource AccentPrimary}"'
    
    # Semantic colors
    'Background="#ED4245"' = 'Background="{StaticResource Danger}"'
    'Foreground="#ED4245"' = 'Foreground="{StaticResource Danger}"'
    'Fill="#ED4245"' = 'Fill="{StaticResource Danger}"'
    'Fill="#57F287"' = 'Fill="{StaticResource Success}"'
    'Fill="#3BA55D"' = 'Fill="{StaticResource Success}"'
    
    # Borders
    'BorderBrush="#3F4147"' = 'BorderBrush="{StaticResource BorderDefault}"'
    'BorderBrush="#1E1F22"' = 'BorderBrush="{StaticResource SidebarBorder}"'
}

$totalReplacements = 0

foreach ($file in $xamlFiles) {
    Write-Host ""
    Write-Host "Processing: $($file.Name)" -ForegroundColor Yellow
    
    # Read file content
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    $fileReplacements = 0
    
    # Apply each color mapping
    foreach ($oldColor in $colorMappings.Keys) {
        $newColor = $colorMappings[$oldColor]
        $matches = ([regex]::Matches($content, [regex]::Escape($oldColor))).Count
        
        if ($matches -gt 0) {
            $content = $content -replace [regex]::Escape($oldColor), $newColor
            $fileReplacements += $matches
            Write-Host "  Replaced $matches instance(s) of $oldColor" -ForegroundColor Green
        }
    }
    
    # Save if changes were made
    if ($content -ne $originalContent) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        $totalReplacements += $fileReplacements
        Write-Host "  Saved $fileReplacements replacements" -ForegroundColor Cyan
    } else {
        Write-Host "  No changes needed" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Complete! Made $totalReplacements replacements across $($xamlFiles.Count) files" -ForegroundColor Green
Write-Host "Run dotnet build to verify everything compiles" -ForegroundColor Yellow

