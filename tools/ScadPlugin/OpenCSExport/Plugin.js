// OpenCS: export of SCAD++ reinforcement selection results for import into OpenCS.
// Writes "<project>.opencs-scad.json" (UTF-16 LE with BOM) next to the *.SPR file.
// Values are written as returned by SCAD together with the unit settings of the
// session (UnitsName/Factor), so that OpenCS converts them itself and does not depend
// on how the user configured units in SCAD.
// Keep this file ASCII-only: the script host encoding is not documented.

var FORMAT_VERSION = 1;

function CheckCall(callResult)
{
	if (callResult)
	{
		throw callResult;
	}
}

function Str(s)
{
	if (s === null || s === undefined)
	{
		return "null";
	}
	s = String(s);
	var out = "\"";
	for (var i = 0; i < s.length; ++i)
	{
		var c = s.charAt(i);
		var code = s.charCodeAt(i);
		if (c === "\"") out += "\\\"";
		else if (c === "\\") out += "\\\\";
		else if (code < 32 || code > 126)
		{
			var hex = code.toString(16);
			while (hex.length < 4) hex = "0" + hex;
			out += "\\u" + hex;
		}
		else out += c;
	}
	return out + "\"";
}

function Num(x)
{
	if (typeof(x) !== "number" || isNaN(x) || !isFinite(x))
	{
		return "null";
	}
	return String(x);
}

function UnitJson(settings, name)
{
	var u = { Precision:0, IsShowExp:0, ZeroLimit:0, UserFriendlyName:"", UnitsName:"", Factor:0 };
	var err = settings.GetUnit(name, u);
	if (err)
	{
		return "{\"id\":" + Str(name) + ",\"error\":" + Str(err) + "}";
	}
	return "{\"id\":" + Str(name) + ",\"name\":" + Str(u.UnitsName) + ",\"title\":" + Str(u.UserFriendlyName) +
		",\"factor\":" + Num(u.Factor) + "}";
}

function Plugin_Cancel(engine)
{
	if (engine)
	{
		engine.Cancel();
	}
}

function Plugin_Execute(engine)
{
	var stream = null;
	try
	{
		var model = engine.GetModel();
		var result = engine.GetResult();
		if (!result)
		{
			throw "Run the plugin in the analysis results mode (postprocessor).";
		}

		var info = { FileName:"", Name:"", Company:"", Customer:"", Object:"", Executor:"" };
		CheckCall(model.GetInfo(info));
		var spr = String(info.FileName);
		var dot = spr.lastIndexOf(".");
		var outPath = (dot > spr.lastIndexOf("\\") ? spr.substring(0, dot) : spr) + ".opencs-scad.json";

		var fso = new ActiveXObject("Scripting.FileSystemObject");
		stream = fso.CreateTextFile(outPath, true, true);

		var settings = engine.GetSettings();
		var units = [ "theResultArmSquare", "theResultForces", "theResultMoments", "theResultDistributedForces",
			"theResultDistributedMoments", "theResultStress", "theLinearSizes", "theSectionSizes" ];
		var unitParts = [];
		for (var ui = 0; ui < units.length; ++ui)
		{
			unitParts.push(UnitJson(settings, units[ui]));
		}

		stream.Write("{\"format\":\"opencs-scad-rebar\",\"version\":" + FORMAT_VERSION +
			",\"project\":" + Str(spr) + ",\"name\":" + Str(info.Name) +
			",\"exported\":" + Str(new Date().toUTCString()) +
			",\"units\":[" + unitParts.join(",") + "]");

		var plateCount = result.GetRCFitPlateQuantityElem();
		var rodCount = result.GetRCFitRodQuantityElem();
		var total = plateCount + rodCount;
		var done = 0;

		// Plates: one result per element.
		stream.Write(",\n\"plates\":[");
		var first = true;
		var n, k;
		for (n = 1; n <= plateCount; ++n)
		{
			var pi = { QuantityRCFitResults:0, IsPlate:0, IsRod:0 };
			if (!result.GetRCFitPlateInfo(n, pi) && pi.QuantityRCFitResults > 0)
			{
				var pr = { AS1:0, AS2:0, AS3:0, AS4:0, ASWx:0, ASWy:0 };
				if (!result.GetRCFitPlateResult(n, pr))
				{
					stream.Write((first ? "\n" : ",\n") + "{\"e\":" + n +
						",\"as\":[" + Num(pr.AS1) + "," + Num(pr.AS2) + "," + Num(pr.AS3) + "," + Num(pr.AS4) +
						"],\"asw\":[" + Num(pr.ASWx) + "," + Num(pr.ASWy) + "]}");
					first = false;
				}
			}
			if ((++done & 1023) === 0) engine.SetProgress(done, total);
		}
		stream.Write("]");

		// Bars: one result per section along the element.
		stream.Write(",\n\"bars\":[");
		first = true;
		for (n = 1; n <= rodCount; ++n)
		{
			var ri = { QuantityRCFitResults:0, IsPlate:0, IsRod:0 };
			if (!result.GetRCFitRodInfo(n, ri) && ri.QuantityRCFitResults > 0)
			{
				var secs = [];
				for (k = 1; k <= ri.QuantityRCFitResults; ++k)
				{
					var rr = { AS1:0, AS2:0, AS3:0, AS4:0, IWx:0, IWy:0 };
					if (result.GetRCFitRodResult(n, k, rr))
					{
						secs.push("null");
					}
					else
					{
						secs.push("{\"as\":[" + Num(rr.AS1) + "," + Num(rr.AS2) + "," + Num(rr.AS3) + "," + Num(rr.AS4) +
							"],\"iw\":[" + Num(rr.IWx) + "," + Num(rr.IWy) + "]}");
					}
				}
				stream.Write((first ? "\n" : ",\n") + "{\"e\":" + n + ",\"sections\":[" + secs.join(",") + "]}");
				first = false;
			}
			if ((++done & 1023) === 0) engine.SetProgress(done, total);
		}
		stream.Write("]\n}\n");
		stream.Close();
		stream = null;
		engine.SetProgress(total, total);
	}
	catch (e)
	{
		if (stream)
		{
			try { stream.Close(); } catch (e2) { }
		}
		engine.Cancel(typeof(e) === "string" ? e : ("OpenCS export: " + (e.description || e.message || e)));
	}
}
