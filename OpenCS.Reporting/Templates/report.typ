#set page(paper: "a4", margin: (top: 22mm, bottom: 18mm, left: 20mm, right: 15mm))
#set text(font: "Noto Sans", size: 10pt, lang: "ru")
#set par(leading: 0.65em, justify: true)
#show heading.where(level: 1): set text(size: 18pt, weight: "bold")
#show heading.where(level: 2): set text(size: 14pt, weight: "bold")
#show heading.where(level: 3): set text(size: 11pt, weight: "bold")

$if(title)$
= $title$
$endif$

$body$
