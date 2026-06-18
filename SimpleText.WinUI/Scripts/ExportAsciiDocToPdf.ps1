param(
    [Parameter(Mandatory=$true)]
    [string]$InputFile,

    [Parameter(Mandatory=$true)]
    [string]$OutputFile
)

# Requires asciidoctor-pdf to be installed
asciidoctor-pdf $InputFile -o $OutputFile
if ($LASTEXITCODE -ne 0) {
    throw "Asciidoctor-pdf failed with exit code $LASTEXITCODE"
}
