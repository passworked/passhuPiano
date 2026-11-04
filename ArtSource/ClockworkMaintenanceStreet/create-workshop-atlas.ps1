# Original vector drafting artwork. Run from this directory or the project root.
# Uses Windows System.Drawing; no downloaded imagery or fonts.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = Join-Path $root 'Assets/Scenes/ClockworkMaintenanceStreet/ArtRefinement/Textures'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$bmp = New-Object System.Drawing.Bitmap 1024,1024
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'
function Brush($hex) { New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex)) }
function Pen($hex,$w=1) { New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml($hex)),$w }
function Label($s,$x,$y,$size,$hex,$family='Segoe Script',$maxWidth=450) {
    $font=New-Object System.Drawing.Font $family,$size,([System.Drawing.FontStyle]::Regular),([System.Drawing.GraphicsUnit]::Pixel)
    if($font.FontFamily.Name -ne $family){throw "Required decorative font missing: $family"}
    while($g.MeasureString($s,$font).Width -gt $maxWidth){
        $size-=.5;$font.Dispose()
        $font=New-Object System.Drawing.Font $family,$size,([System.Drawing.FontStyle]::Regular),([System.Drawing.GraphicsUnit]::Pixel)
    }
    $b=Brush $hex; $g.DrawString($s,$font,$b,$x,$y);$font.Dispose();$b.Dispose()
}
$cream=Brush '#D6C8A6';$green=Brush '#173F3E';$burgundy=Brush '#643B36'
$g.FillRectangle($green,0,0,512,512);$g.FillRectangle($cream,512,0,512,512)
$g.FillRectangle($green,0,512,512,512);$g.FillRectangle($burgundy,512,512,512,512)
$gold=Pen '#BEA274' 1.5; $fine=Pen '#789086' .7; $ink=Pen '#526150' 1
# Blueprint grid and drafted coupled wheels.
for($i=32;$i -lt 500;$i+=24){$g.DrawLine($fine,$i,74,$i,440);if($i+48 -lt 440){$g.DrawLine($fine,24,$i+48,488,$i+48)}}
Label "L’arte del tempo" 26 9 37 '#DCC99D'
Label 'Scappamento · Officina n. 04' 27 49 15 '#ADBAA9'
foreach($wheel in @(@(170,230,92,32),@(325,230,61,24),@(236,370,48,20))){
    $cx=$wheel[0];$cy=$wheel[1];$r=$wheel[2];$n=$wheel[3]
    $g.DrawEllipse($gold,$cx-$r,$cy-$r,$r*2,$r*2);$g.DrawEllipse($gold,$cx-16,$cy-16,32,32)
    $g.DrawEllipse($fine,$cx-$r+10,$cy-$r+10,($r-10)*2,($r-10)*2)
    for($k=0;$k -lt $n;$k++){$a=$k*2*[Math]::PI/$n;$g.DrawLine($gold,[single]($cx+($r-5)*[Math]::Cos($a)),[single]($cy+($r-5)*[Math]::Sin($a)),[single]($cx+($r+7)*[Math]::Cos($a)),[single]($cy+($r+7)*[Math]::Sin($a)))}
    for($k=0;$k -lt 6;$k++){$a=$k*[Math]::PI/3;$g.DrawLine($gold,[single]($cx+20*[Math]::Cos($a)),[single]($cy+20*[Math]::Sin($a)),[single]($cx+($r-16)*[Math]::Cos($a)),[single]($cy+($r-16)*[Math]::Sin($a)))}
    $g.DrawLine($fine,$cx-12,$cy,$cx+12,$cy);$g.DrawLine($fine,$cx,$cy-12,$cx,$cy+12)
}
$g.DrawLine($gold,78,105,378,105);$g.DrawLine($gold,78,95,78,118);$g.DrawLine($gold,378,95,378,118)
Label 'm 0,16 · 20°' 169 84 14 '#DCC99D' 'Segoe Script' 195
Label 'Tavola IV · Studio del movimento' 28 457 18 '#DCC99D'
# Service ledger, legible at desk distance.
Label 'Il custode del tempo' 537 15 32 '#354B40'
Label 'Diario di bottega · n. 04' 542 64 18 '#75664E'
$g.DrawLine($ink,540,99,995,99)
Label 'Ora          Lavori eseguiti' 542 109 18 '#4B5D50'
$rows=@('06:00    Caricare il meccanismo','09:15    Controllare lo scappamento','12:00    Oliare i cuscinetti','15:30    Regolare il pendolo','18:00    Accordare per il concerto','24:00    Tutto è in ordine')
for($i=0;$i -lt $rows.Count;$i++){Label $rows[$i] 541 (150+$i*42) 18 '#655D49';$g.DrawLine($ink,542,(188+$i*42),992,(188+$i*42))}
Label 'Il tempo passa, la musica resta.' 540 441 22 '#7A5641'
# Engraved medallion: concentric rings and watch indices.
foreach($r in @(219,209,180,171,60)){$g.DrawEllipse($gold,256-$r,768-$r,$r*2,$r*2)}
for($i=0;$i -lt 60;$i++){$a=$i*[Math]::PI/30;$r0=if($i%5 -eq 0){184}else{193};$g.DrawLine($gold,[single](256+$r0*[Math]::Sin($a)),[single](768-$r0*[Math]::Cos($a)),[single](256+204*[Math]::Sin($a)),[single](768-204*[Math]::Cos($a)))}
Label 'Bottega' 159 644 40 '#D7BD86' 'Segoe Script' 205
Label '04' 210 729 55 '#D7BD86' 'Segoe Script' 110
Label "d’orologeria" 121 838 32 '#D7BD86' 'Segoe Script' 280
# Carpet: quiet linework, clock-inspired corners and fine woven border.
foreach($inset in @(14,23,36)){$g.DrawRectangle($gold,512+$inset,512+$inset,512-2*$inset,512-2*$inset)}
foreach($x in @(576,960)){foreach($y in @(576,960)){$g.DrawEllipse($gold,$x-20,$y-20,40,40);$g.DrawLine($gold,$x-27,$y,$x+27,$y);$g.DrawLine($gold,$x,$y-27,$x,$y+27)}}
$g.DrawEllipse($gold,698,698,140,140);$g.DrawEllipse($gold,708,708,120,120)
for($i=0;$i -lt 12;$i++){$a=$i*[Math]::PI/6;$g.DrawLine($gold,[single](768+48*[Math]::Sin($a)),[single](768-48*[Math]::Cos($a)),[single](768+58*[Math]::Sin($a)),[single](768-58*[Math]::Cos($a)))}
$g.DrawLine($gold,768,768,768,731);$g.DrawLine($gold,768,768,795,787)
$bmp.Save((Join-Path $out 'WorkshopAtlas.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$bmp.Dispose();$gold.Dispose();$fine.Dispose();$ink.Dispose();$cream.Dispose();$green.Dispose();$burgundy.Dispose()
Write-Output 'WorkshopAtlas.png generated (original drafting and carpet artwork).'
