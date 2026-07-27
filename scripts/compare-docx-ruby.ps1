[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string[]]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function ConvertTo-NormalizedXml {
    param([Parameter(Mandatory)][System.Xml.XmlNode]$Node)

    switch ($Node.NodeType) {
        ([System.Xml.XmlNodeType]::Element) {
            $attributes = @(
                $Node.Attributes |
                    Sort-Object -Property Name |
                    ForEach-Object {
                        ' {0}="{1}"' -f $_.Name, [System.Security.SecurityElement]::Escape($_.Value)
                    })
            $children = @(
                $Node.ChildNodes |
                    ForEach-Object {
                        if ($_.NodeType -eq [System.Xml.XmlNodeType]::Text) {
                            $text = [regex]::Replace($_.Value, '\s+', ' ').Trim()
                            if ($text) { [System.Security.SecurityElement]::Escape($text) }
                        }
                        elseif ($_.NodeType -eq [System.Xml.XmlNodeType]::Element) {
                            ConvertTo-NormalizedXml -Node $_
                        }
                    })
            if ($children.Count -eq 0) {
                return '<{0}{1}/>' -f $Node.Name, ($attributes -join '')
            }

            return '<{0}{1}>{2}</{0}>' -f $Node.Name, ($attributes -join ''), ($children -join '')
        }
        default {
            return ''
        }
    }
}

function Read-ZipXml {
    param(
        [Parameter(Mandatory)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory)][string]$EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) { return $null }

    $reader = [System.IO.StreamReader]::new($entry.Open(), [System.Text.Encoding]::UTF8, $true)
    try {
        $xml = [System.Xml.XmlDocument]::new()
        $xml.PreserveWhitespace = $false
        $xml.LoadXml($reader.ReadToEnd())
        return $xml
    }
    finally {
        $reader.Dispose()
    }
}

$missingFile = $false
foreach ($docxPath in $Path) {
    if (-not [System.IO.File]::Exists($docxPath)) {
        Write-Error "DOCX file was not found: $docxPath"
        $missingFile = $true
        continue
    }

    $fullPath = [System.IO.Path]::GetFullPath($docxPath)
    $stream = [System.IO.File]::Open($fullPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Read, $false)
        try {
            $documentXml = Read-ZipXml -Archive $archive -EntryName 'word/document.xml'
            $stylesXml = Read-ZipXml -Archive $archive -EntryName 'word/styles.xml'

            Write-Output "# $fullPath"
            Write-Output '## word/document.xml: w:ruby'
            if ($null -eq $documentXml) {
                Write-Output '(missing)'
            }
            else {
                $namespaceManager = [System.Xml.XmlNamespaceManager]::new($documentXml.NameTable)
                $namespaceManager.AddNamespace('w', 'http://schemas.openxmlformats.org/wordprocessingml/2006/main')
                $rubies = $documentXml.SelectNodes('//w:ruby', $namespaceManager)
                if ($rubies.Count -eq 0) {
                    Write-Output '(none)'
                }
                else {
                    foreach ($ruby in $rubies) {
                        Write-Output (ConvertTo-NormalizedXml -Node $ruby)
                    }
                }
            }

            Write-Output '## word/styles.xml: ruby-relevant styles'
            if ($null -eq $stylesXml) {
                Write-Output '(missing)'
            }
            else {
                $namespaceManager = [System.Xml.XmlNamespaceManager]::new($stylesXml.NameTable)
                $namespaceManager.AddNamespace('w', 'http://schemas.openxmlformats.org/wordprocessingml/2006/main')
                $styles = $stylesXml.SelectNodes('//w:docDefaults[.//w:rPr] | //w:style[.//w:rPr]', $namespaceManager)
                if ($styles.Count -eq 0) {
                    Write-Output '(none)'
                }
                else {
                    foreach ($style in $styles) {
                        Write-Output (ConvertTo-NormalizedXml -Node $style)
                    }
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

if ($missingFile) {
    exit 1
}
