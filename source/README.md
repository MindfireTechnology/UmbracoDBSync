# UmbracoDBSync
Automatically sync documents to a database using Entity Framework

# Installation
All you need to do is install the NuGet package and then customize your TableMappings.config file. 

Example Mapping:

	<?xml version="1.0" encoding="utf-8" ?>
	<mappings dataContext="UgeData.UtahGeekEventsEntities" assembly="UgeData" namespace="UgeData">
		<table name="Events" documentType="event" entityTypeName="Event" entityPropertyName="Events" autoMap="true">
			<field name="EventID" dataType="Integer" key="true"/>
			<field name="SiteID" defaultValue="1" dataType="Integer" />
			<field name="EventMoniker" alias="Name" />
			<field name="EventName" alias="Name" />
			<!--The rest of the fields are AutoMapped-->
		</table>

		<table name="Presentations" namespace="UgeData" assembly="UgeData" documentType="Presentation" entityTypeName="Presentation" entityPropertyName="Presentations" autoMap="true">
			<field name="PresentationID" dataType="Integer" key="true"/>
			<field name="EventID" dataType="Integer" inherit="true"/>
			<field name="SuggestedByUserID" alias="UserID" defaultValue="5" dataType="Integer" inherit="true"/>
			<field name="TrackID" defaultValue="1" dataType="Integer" />
			<field alias="Track" />
			<field name="AddedDateTime" dataType="DateTimeStamp" defaultValue="now()" />
			<field name="IsSuggestion" defaultValue="false" dataType="Boolean" />
			<field name="IsConfirmed" defaultValue="false" dataType="Boolean" />
			<field name="IsScheduled" defaultValue="false" dataType="Boolean" />
			<field name="Visible" isEnabledColumn="true" defaultValue="true" dataType="Boolean" />
		</table>

	</mappings>

