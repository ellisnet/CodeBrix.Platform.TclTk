/*
 * AssemblyInfo.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 */

using System.Reflection;
using System.Resources;

#if INTERNALS_VISIBLE_TO
using System.Runtime.CompilerServices;
#endif

using System.Runtime.InteropServices;

#if NATIVE && !NET_40
using System.Security.Permissions;
#endif

using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Private;


#if INTERNALS_VISIBLE_TO
using CodeBrix.Platform.TclTk._Components.Private;
#endif

using CodeBrix.Platform.TclTk._Components.Shared;
using CodeBrix.Platform.TclTk._Constants;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("TclTk")]
[assembly: AssemblyDescription("Extensible Adaptable Generalized Logic Engine")]
[assembly: AssemblyCompany("TclTk Development Team")]
[assembly: AssemblyProduct("TclTk")]
[assembly: AssemblyCopyright("Copyright © 2007-2012 by Joe Mistachkin.  All rights reserved.")]
[assembly: NeutralResourcesLanguage("en-US")]

#if NATIVE && !NET_40
[assembly: SecurityPermission(SecurityAction.RequestMinimum, Unrestricted = true)]
#endif

#if DEBUG
[assembly: AssemblyConfiguration(BuildConfiguration.Debug)]
#else
[assembly: AssemblyConfiguration(BuildConfiguration.Release)]
#endif

//
// HACK: We may (at some point) want the toolkit assembly to act as part of the
//       core library; therefore, it needs access to our internals (e.g. its
//       commands can derive from _Commands.Core instead of _Commands.Default).
//
#if INTERNALS_VISIBLE_TO
[assembly: InternalsVisibleTo("TclTkToolkit, PublicKey=" + InternalKeys.Fast)]
[assembly: InternalsVisibleTo("TclTkToolkit, PublicKey=" + InternalKeys.Strong)]
[assembly: InternalsVisibleTo("TclTkToolkit, PublicKey=" + InternalKeys.Beta)]
#endif

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("27f23758-f5d2-48cc-a695-c2c24dda8fdf")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Revision and Build Numbers
// by using the '*' as shown below:
#if !PATCHLEVEL
[assembly: AssemblyVersion("1.0.*")]
#endif

//
// NOTE: Custom attributes for this assembly.
//
#if !ASSEMBLY_DATETIME
[assembly: AssemblyDateTime()] /* YES: EXEMPT. */
#endif

[assembly: AssemblyTag("beta")]

#if OFFICIAL_BINARY
[assembly: AssemblyLicense(BinaryLicense.Summary, BinaryLicense.Text)]
#else
[assembly: AssemblyLicense(SourceLicense.Summary, SourceLicense.Text)]
#endif

[assembly: AssemblyUri("https://urn.to/r/tcltk")]
[assembly: AssemblyUri("xmlSchema", "https://tcltk.to/2009/schema")]
[assembly: AssemblyUri("update", "https://update.tcltk.to/")]
[assembly: AssemblyUri("download", "https://download.tcltk.to/")]
[assembly: AssemblyUri("script", "https://script.tcltk.to/")]
[assembly: AssemblyUri("auxiliary", "https://urn.to/r")]
[assembly: AssemblyUri("license", "https://urn.to/r/license")]
[assembly: AssemblyUri("provision", "https://urn.to/r/provision")]

#if NETWORK && OFFICIAL_BINARY && !ENTERPRISE_LOCKDOWN
[assembly: AssemblyUri("trustedRemote", "https://urn.to/r/auto")]
#endif
