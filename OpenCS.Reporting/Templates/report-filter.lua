-- Форматоспецифичные элементы отчёта OpenCS.
-- Фильтр работает только с AST Pandoc и не читает сеть или внешние файлы.

function Div(element)
  local is_page_break = false
  for _, class in ipairs(element.classes) do
    if class == "page-break" then
      is_page_break = true
      break
    end
  end

  if not is_page_break then
    return nil
  end

  if FORMAT:match("docx") then
    return pandoc.RawBlock("openxml", '<w:p xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:r><w:br w:type="page"/></w:r></w:p>')
  end

  if FORMAT:match("typst") or FORMAT:match("pdf") then
    return pandoc.RawBlock("typst", "#pagebreak()")
  end

  return nil
end
